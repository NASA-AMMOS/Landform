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
        [ConfigEnvironmentVariable("LANDFORM_PLACES_USERNAME")]
        public string Username { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_PLACES_API_KEY")]
        public string APIKey { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_NAME")]
        public string AuthCookieName { get; set; } = "ssosession";

        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_VALUE")]
        public string AuthCookieValue { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_PLACES_AUTH_COOKIE_FILE")]
        public string AuthCookieFile { get; set; } = "~/.cssotoken/m20-dev/ssosession";

        [ConfigEnvironmentVariable("LANDFORM_PLACES_VIEW")]
        public string View { get; set; } = "best_tactical"; //MSL: best_tactical, localized_pos, localized_interp 

        [ConfigEnvironmentVariable("LANDFORM_PLACES_URL")]
        public string Url { get; set; } = "https://places-dev.m20-dev.jpl.nasa.gov"; //M2020 dev copy of MSL data
        //https://mslplaces.jpl.nasa.gov:9443/msl-ops/places //MSL mission server - don't use for dev
        //https://places-external-roastt.m20-training.jpl.nasa.gov/m2020-places //ROASTT
        //https://places-sstage.m20.jpl.nasa.gov //TT4 - requires m2020-prod-gov credentials

        [ConfigEnvironmentVariable("LANDFORM_PLACES_RESPONSE_TYPE")]
        public string ResponseType { get; set; } = "application/xml"; //application/xml or application/json (experimental)

        public override string ConfigFileName()
        {
            return "places";
        }
    }

    /// <summary>
    /// Places is a service that JPL runs for storing and reporting
    /// different position estimates of spacecraft such as rovers.
    /// This class interfaces with the MSL version of places to compute relative 
    /// rover positions between site drives
    /// </summary>
    public class MSLPlaces
    {
        private ILogger logger;

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

        public MSLPlaces(ILogger logger = null)
        {
            this.logger = logger;

            var config = PlacesConfig.Instance;

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
                
            view = config.View;

            try
            {
                GetEstimatedOffsetToStart(new SiteDrive(1, 0)); //test query
            }
            catch
            {
                if (logger != null)
                {
                    logger.LogError("PlacesDB test query for sitedrive (1, 0) failed");
                }
                throw;
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
            }
        }

        private string Fetch(string url)
        {
            lock (cache)
            {
                if (cache.ContainsKey(url))
                {
                    var doc = cache[url];
                    if (doc == null)
                    {
                        throw new Exception(string.Format("PlacesDB: request '{0}' failed, not retrying", url));
                    }
                    return doc;
                }

                var config = PlacesConfig.Instance;
                
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
                request.Resource = url;

                if (!string.IsNullOrEmpty(config.ResponseType))
                {
                    request.AddHeader("Accept", config.ResponseType);
                }
                
                IRestResponse response = client.Execute(request);
                
                if (response.ResponseStatus != ResponseStatus.Completed ||
                    response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    cache[url] = null;
                    throw new Exception(string.Format("PlacesDB: {0} connecting for request '{1}': {2}",
                                                      response.StatusCode, url, response.ErrorMessage));
                }
                
                string content = response.Content;
                cache[url] = content;

                Debug("MSLPlaces request: {0}, response:\n{1}", url, content);

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

        private XmlDocument ParseXml(string url, string response)
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
                                                  url, ex.Message));
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

        private JsonDocument ParseJson(string url, string response)
        {
            return JsonHelper.FromJson<JsonDocument>(response);
        }

        private Vector3 GetOffset(string url)
        {
            string response = Fetch(url);
            Vector3 offset = new Vector3();
            if (response.StartsWith("{"))
            {
                JsonDocument doc = ParseJson(url, response);
                var translations = doc.translations;
                if (translations.Length != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(translations[0].offset[0], translations[0].offset[1], translations[0].offset[2]);
            }
            else
            {
                XmlDocument doc = ParseXml(url, response);
                XmlNodeList nodes = doc.GetElementsByTagName("offset");
                if (nodes.Count != 1)
                {
                    throw new Exception("PlacesDB: unexpected number of offsets in response");
                }
                offset = new Vector3(double.Parse(nodes[0].Attributes["x"].Value),
                                     double.Parse(nodes[0].Attributes["y"].Value),
                                     double.Parse(nodes[0].Attributes["z"].Value));
            }

            Debug("MSLPlaces request {0}, offset {1}", url, offset);

            return offset;
        }

        private double GetEllipsoidRadius()
        {
            string url = string.Format("rmc/orbital(0)/metadata");
            string response = Fetch(url);
            double radius = 0;
            if (response.StartsWith("{"))
            {
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/752
                throw new Exception("PlacesDB: orbital metadata Json TODO");
            }
            else
            {
                XmlDocument doc = ParseXml(url, response);
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

            Debug("MSLPlaces request {0}, radius {1}", url, radius);

            return radius;
        }

        /// <summary>
        /// Finds the estimated mars lat and lon for a given site drive
        /// </summary>
        public Vector2 GetEstimatedLatLon(SiteDrive sd)
        {
            if (!ellipsoidRadius.HasValue)
            {
                throw new Exception("PlacesDB: ellipsoid radius not available");
            }
            string url = string.Format("query/primary/{0}?from=rover({1},{2})&to=orbital(0)", view, sd.Site, sd.Drive);
            Vector3 v = GetOffset(url);
            // x is northing, y is easting
            double lat = MathHelper.ToDegrees(v.X / ellipsoidRadius.Value);
            double lon = MathHelper.ToDegrees(v.Y / ellipsoidRadius.Value);
            return new Vector2(lat, lon);
        }

        /// <summary>
        /// Returns the Local_level frame offset between the "from" sitedrive to the "to" site
        /// </summary>
        public Vector3 GetEstimatedOffsetToSite(SiteDrive fromSD, int toSite)
        {
            string url = null;
            if (fromSD.Drive > 0)
            {
                url = string.Format("query/primary/{0}?from=rover({1},{2})&to=site({3})", view, fromSD.Site, fromSD.Drive, toSite);
            }
            else
            {
                url = string.Format("query/primary/{0}?from=site({1})&to=site({2})", view, fromSD.Site, toSite);
            }
            return GetOffset(url);
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
