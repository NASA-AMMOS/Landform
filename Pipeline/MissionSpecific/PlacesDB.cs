//#define DEBUG_PLACES
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Xna.Framework;
using OPS.Util;
using RestSharp;
using RestSharp.Authenticators;

namespace OPS.Pipeline
{
    public class PlacesConfig : SingletonConfig<PlacesConfig>
    {
        public const string CONFIG_FILENAME = "places"; //config file will be ~/.landform/places.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        //PLACES instance URL
        //default is null which disables PlacesDB
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_URL")]
        public string Url { get; set; }

        //PLACES solution view
        //default is null which disables PlacesDB
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_VIEW")]
        public string View { get; set; }

        //username for http basic auth
        //default is null whcih means disable basic auth
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_USERNAME")]
        public string Username { get; set; }

        //password for http basic auth
        //default is null whcih means disable basic auth
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_API_KEY")]
        public string APIKey { get; set; }

        //name of auth cookie
        //null means no auth cookie
        //default is "ssosession"
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_NAME")]
        public string AuthCookieName { get; set; } = "ssosession";
 
        //auth cookie
        //default is null which means read from file, if any
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_VALUE")]
        public string AuthCookieValue { get; set; }

        //read auth cookie from file
        //default is null which disables auth cookie file
        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_FILE")]
        public string AuthCookieFile { get; set; } = "~/.cssotoken/dev-old/ssosession";

        //default may be overridden by MissionSpecific.GetPlacesConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_PLACES_RESPONSE_TYPE")]
        public string ResponseType { get; set; } = "application/xml"; //application/xml or application/json (experimental)
    }

    /// <summary>
    /// PLACES is a service that JPL runs for storing and reporting position estimates of spacecraft such as rovers.
    /// This class interfaces with PLACES to compute relative rover positions between site drives.
    /// </summary>
    public class PlacesDB
    {
        public string FALLBACK_VIEW = "telemetry";

        private ILogger logger;

        private PlacesConfig config;

        private string view;
        private string cookieValue;

        private double? ellipsoidRadius;

        //avoid hitting the upstream service too hard
        //important: this is explicitly *not* a ConcurrentDictionary
        //we lock on it to serialize requests
        //that handles the case of launching multiple initial requests for the same query in parallel
        //query => response
        Dictionary<string, string> cache = new Dictionary<string, string>();

        private ConcurrentDictionary<SiteDrive, Vector3> cachedOffsetFromStart =
            new ConcurrentDictionary<SiteDrive, Vector3>();

        public PlacesDB(ILogger logger = null, bool requireOrbital = false)
        {
            this.logger = logger;

            config = PlacesConfig.Instance;

            if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.View))
            {
                throw new Exception("no PLACES database for mission");
            }

            if (!string.IsNullOrEmpty(config.AuthCookieValue))
            {
                cookieValue = config.AuthCookieValue;
            }
            else if (!string.IsNullOrEmpty(config.AuthCookieFile))
            {
                string path = config.AuthCookieFile;
                if (path.StartsWith("~"))
                {
                    path = Path.Combine(PathHelper.GetHomeDir(), path.Substring(2));
                }
                if (File.Exists(path))
                {
                    cookieValue = File.ReadAllText(path);
                }
            }

            try
            {
                view = config.View;
                GetEstimatedOffsetToStart(new SiteDrive(1, 0)); //test query
            }
            catch
            {
                if (logger != null)
                {
                    logger.LogWarn("PlacesDB test query for sitedrive (1, 0) failed, URL {0}, view {1}",
                                   config.Url, view);
                }
                view = FALLBACK_VIEW;
                logger.LogWarn("trying fallback view {0}", view);
                try
                {
                    GetEstimatedOffsetToStart(new SiteDrive(1, 0));
                }
                catch
                {
                    if (logger != null)
                    {
                        logger.LogError("PlacesDB test query for sitedrive (1, 0) failed, URL {0}, view {1}",
                                        config.Url, view);
                    }
                    throw;
                }
            }

            try
            {
                ellipsoidRadius = GetEllipsoidRadius();
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError("error getting ellipsoid radius from PlacesDB: {0}", ex.Message);
                }
                if (requireOrbital)
                {
                    throw;
                }
            }
        }

        private string Fetch(string query)
        {
            lock (cache)
            {
                if (cache.ContainsKey(query))
                {
                    var doc = cache[query];
                    if (doc == null)
                    {
                        throw new Exception(string.Format("PlacesDB: query {0} failed, not retrying", query));
                    }
                    return doc;
                }

                Uri uri = new Uri(config.Url);
                
                RestClient client = new RestClient();
                client.BaseUrl = uri;
                
                if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.APIKey))
                {
                    client.Authenticator = new HttpBasicAuthenticator(config.Username, config.APIKey);
                }
                
                if (!string.IsNullOrEmpty(config.AuthCookieName) && !string.IsNullOrEmpty(cookieValue))
                {
                    client.CookieContainer = new CookieContainer();
                    var cookie = new Cookie(config.AuthCookieName, cookieValue) { Domain = uri.Host };
                    client.CookieContainer.Add(cookie);
                }
                
                var request = new RestRequest();
                request.Resource = query;

                if (!string.IsNullOrEmpty(config.ResponseType))
                {
                    request.AddHeader("Accept", config.ResponseType);
                }
                
                IRestResponse response = client.Execute(request);
                
                if (response.ResponseStatus != ResponseStatus.Completed ||
                    response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    cache[query] = null;
                    throw new Exception(string.Format("PlacesDB: {0} connecting for request {1}: {2}",
                                                      response.StatusCode, config.Url + "/" + query,
                                                      response.ErrorMessage));
                }
                
                string content = response.Content;
                cache[query] = content;

                Debug("PlacesDB request: {0}, response:\n{1}", config.Url + "/" + query, content);

                return content;
            }
        }

        private void Debug(string msg, params Object[] args)
        {
#if DEBUG_PLACES
            if (logger != null)
            {
                logger.LogInfo(msg, args);
            }
            else
            {
                Console.WriteLine(msg, args);
            }
#endif
        }

        private XmlDocument ParseXml(string query, string response)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(response);
                return doc;
            }
            catch (System.Xml.XmlException ex)
            {
                throw new Exception(string.Format("PlacesDB: error parsing response for request {0}: {1}",
                                                  query, ex.Message));
            }
        }

        private class JsonTranslation
        {
            public double[] offset = new double[0];
        }

        private class JsonDocument
        {
            public JsonTranslation[] translations = new JsonTranslation[0];
        }

        private JsonDocument ParseJson(string query, string response)
        {
            return JsonHelper.FromJson<JsonDocument>(response);
        }

        private Vector3 GetOffset(string query)
        {
            string response = Fetch(query);
            Vector3 offset = new Vector3();
            if (response.StartsWith("{"))
            {
                JsonDocument doc = ParseJson(query, response);
                var translations = doc.translations;
                if (translations.Length != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(translations[0].offset[0], translations[0].offset[1], translations[0].offset[2]);
            }
            else
            {
                XmlDocument doc = ParseXml(query, response);
                XmlNodeList nodes = doc.GetElementsByTagName("offset");
                if (nodes.Count != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(double.Parse(nodes[0].Attributes["x"].Value),
                                     double.Parse(nodes[0].Attributes["y"].Value),
                                     double.Parse(nodes[0].Attributes["z"].Value));
            }

            Debug("PlacesDB request: {0}, offset {1}", query, offset);

            return offset;
        }

        private double GetEllipsoidRadius()
        {
            string query = string.Format("rmc/orbital(0)/metadata");
            string response = Fetch(query);
            double radius = 0;
            if (response.StartsWith("{"))
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/752
                throw new Exception("PlacesDB: orbital metadata Json TODO");
            }
            else
            {
                XmlDocument doc = ParseXml(query, response);
                bool ok = false;
                foreach (XmlElement itemNode in doc.GetElementsByTagName("item"))
                {
                    XmlNodeList elList = itemNode.GetElementsByTagName("key");
                    if (elList.Count == 1 && elList[0].InnerText.Contains("ellipsoid_radius"))
                    {
                        radius = double.Parse(itemNode.GetElementsByTagName("value")[0].InnerText);
                        ok = true;
                        break;
                    }
                }
                if (!ok)
                {
                    throw new Exception("PlacesDB: ellipsoid_radius not found in orbital metadata");
                }
            }

            Debug("PlacesDB request {0}, radius {1}", query, radius);

            return radius;
        }

        /// <summary>
        /// Finds the estimated mars lat and lon for a given site drive
        /// returned X = longitude, Y = latitude
        /// </summary>
        public Vector2 GetEstimatedLatLon(SiteDrive sd, int orbitalIndex = 0, string orbitalFileName=null)
        {
            if (!ellipsoidRadius.HasValue)
            {
                throw new Exception("PlacesDB: ellipsoid radius not available");
            }
            string query = string.Format("query/primary/{0}?from=rover({1},{2})&to=orbital({3})",
                                         view, sd.Site, sd.Drive, orbitalIndex);
            Vector3 v = GetOffset(query);
            // x is northing, y is easting for orbital image 0 MSL
            double lat = MathHelper.ToDegrees(v.X / ellipsoidRadius.Value);
            double lon = MathHelper.ToDegrees(v.Y / ellipsoidRadius.Value);
            return new Vector2(lon, lat);
        }

        /// <summary>
        /// Returns the Local_level frame offset between the "from" sitedrive to the "to" site
        /// </summary>
        public Vector3 GetEstimatedOffsetToSite(SiteDrive fromSD, int toSite)
        {
            string query = null;
            if (fromSD.Drive > 0)
            {
                query = string.Format("query/primary/{0}?from=rover({1},{2})&to=site({3})",
                                      view, fromSD.Site, fromSD.Drive, toSite);
            }
            else
            {
                query = string.Format("query/primary/{0}?from=site({1})&to=site({2})", view, fromSD.Site, toSite);
            }
            return GetOffset(query);
        }

        /// <summary>
        /// Finds the offset from the landing site to the current site drive
        /// </summary>
        /// <param name="sd"></param>
        /// <returns></returns>
        public Vector3 GetEstimatedOffsetToStart(SiteDrive sd)
        {
            return cachedOffsetFromStart.GetOrAdd(sd, _ => GetEstimatedOffsetToSite(sd, 1));
        }
    }
}
