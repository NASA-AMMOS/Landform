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
    public class MSLPlacesException : Exception
    {
        public MSLPlacesException(string msg) : base(msg) { }
    }

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
        public string AuthCookieFile { get; set; } = "~/.cssotoken/ssosession";

        [ConfigEnvironmentVariable("LANDFORM_PLACES_VIEW")]
        public string View { get; set; } = "localized_interp"; // options: best_tactical, localized_pos, localized_interp 

        [ConfigEnvironmentVariable("LANDFORM_PLACES_URL")]
        public string Url { get; set; } = "https://places-dev.m20-dev.jpl.nasa.gov"; //M2020 dev copy of MSL data
        //"https://mslplaces.jpl.nasa.gov:9443/msl-ops/places"; //MSL mission server - don't use for dev

        protected override string ConfigFilename()
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
        private string view;
        private string cookieValue;

        private double ellipsoidRadius;

        //avoid hitting the upstream service too hard
        //important: this is explicitly *not* a ConcurrentDictionary
        //we lock on it to serialize requests
        //that handles the case of launching multiple initial requests for the same query in parallel
        //query => response
        Dictionary<string, XmlDocument> cache = new Dictionary<string, XmlDocument>();

        private ConcurrentDictionary<SiteDrive, Vector3> cachedOffsetFromStart =
            new ConcurrentDictionary<SiteDrive, Vector3>();

        public MSLPlaces()
        {
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
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    path = Path.Combine(home, path.Substring(2));
                }
                if (File.Exists(path))
                {
                    cookieValue = File.ReadAllText(path);
                }
            }
                
            view = config.View;

            ellipsoidRadius = GetEllipsoidRadius(); //also serves as test query
        }

        private XmlDocument Fetch(string url)
        {
            lock (cache)
            {
                if (cache.ContainsKey(url))
                {
                    var doc = cache[url];
                    if (doc == null)
                    {
                        throw new Exception(string.Format("Places DB request '{0}' failed, not retrying", url));
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
                
                IRestResponse response = client.Execute(request);
                
                if (response.ResponseStatus != ResponseStatus.Completed ||
                    response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    cache[url] = null;
                    throw new Exception(string.Format("{0} connecting to Places DB for request '{1}': {2}",
                                                      response.StatusCode, url, response.ErrorMessage));
                }
                
                XmlDocument document = new XmlDocument();
                try
                {
                    document.LoadXml(response.Content);
                }
                catch (System.Xml.XmlException ex)
                {
                    cache[url] = null;
                    throw new Exception(string.Format("Error parsing response from Places DB for request '{0}': {1}",
                                                      url, ex.Message));
                }
                
                cache[url] = document;

                return document;
            }
        }
                
        private Vector3 GetOffset(XmlDocument doc)
        {
            XmlNodeList nodes = doc.GetElementsByTagName("offset");
            if (nodes.Count != 1)
            {
                throw new MSLPlacesException("Unexpected number of offsets in places query");
            }
            return new Vector3(double.Parse(nodes[0].Attributes["x"].Value),
                               double.Parse(nodes[0].Attributes["y"].Value),
                               double.Parse(nodes[0].Attributes["z"].Value));
        }

        private double GetEllipsoidRadius()
        {
            string url = string.Format("rmc/orbital(0)/metadata");
            XmlDocument document = Fetch(url);
            if (document != null)
            {
                foreach (XmlElement itemNode in document.GetElementsByTagName("item"))
                {
                    XmlNodeList elList = itemNode.GetElementsByTagName("key");
                    if (elList.Count == 1 && elList[0].InnerText.Contains("ellipsoid_radius"))
                    {
                        return double.Parse(itemNode.GetElementsByTagName("value")[0].InnerText);
                    }
                }
            }
            throw new Exception("failed to get ellipsoid radius from PlacesDB");
        }

        /// <summary>
        /// Finds the estimated mars lat and lon for a given site drive
        /// </summary>
        public Vector2 GetEstimatedLatLon(SiteDrive sd)
        {
            string url = string.Format("query/primary/{0}?from=rover({1},{2})&to=orbital(0)", view, sd.Site, sd.Drive);
            Vector3 v = GetOffset(Fetch(url));
            // x is northing, y is easting
            double lat = MathHelper.ToDegrees(v.X / ellipsoidRadius);
            double lon = MathHelper.ToDegrees(v.Y / ellipsoidRadius);
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
            return GetOffset(Fetch(url));
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
