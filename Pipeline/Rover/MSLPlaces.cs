using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        [ConfigEnvironmentVariable("LANDFORM_PLACES_VENUE")]
        public string Venue { get; set; } = "msl-ops";

        [ConfigEnvironmentVariable("LANDFORM_PLACES_VIEW")]
        public string View { get; set; } = "localized_interp"; // options: best_tactical, localized_pos, localized_interp 

        [ConfigEnvironmentVariable("LANDFORM_PLACES_URL")]
        public string Url { get; set; } = "https://mslplaces.jpl.nasa.gov:9443";

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

        private double? EllipsoidRadius = null;

        private XmlDocument GetXmlDoc(string url)
        {
            var config = PlacesConfig.Instance;
            RestClient client = new RestClient();
            client.BaseUrl = new Uri(config.Url);
            client.Authenticator = new HttpBasicAuthenticator(config.Username, config.APIKey);
            var request = new RestRequest();
            
            request.Resource = url;
            IRestResponse response = client.Execute(request);
            if(response.ResponseStatus != ResponseStatus.Completed)
            {
                throw new Exception("Error connecting to places: " + response.StatusCode.ToString() + " " + response.ErrorMessage);
            }
            XmlDocument document = new XmlDocument();
            document.LoadXml(response.Content);
            return document;
        }

        private Vector3 ReadOffsetFromDocument(XmlDocument doc)
        {
            XmlNodeList nodes = doc.GetElementsByTagName("offset");
            if (nodes.Count != 1)
            {
                throw new MSLPlacesException("Unexpected number of offsets in places query");
            }
            XmlNode offsetNode = nodes[0];
            Vector3 v = new Vector3();
            v.X = double.Parse(offsetNode.Attributes["x"].Value);
            v.Y = double.Parse(offsetNode.Attributes["y"].Value);
            v.Z = double.Parse(offsetNode.Attributes["z"].Value);
            return v;
        }

        private double GetElipsoidRadius()
        {
            if (EllipsoidRadius == null)
            {
                var config = PlacesConfig.Instance;

                string urlForRequest = string.Format("{0}/places/rmc/orbital(0)/metadata", config.Venue);
                XmlDocument document = GetXmlDoc(urlForRequest);
                foreach (XmlElement itemNode in document.GetElementsByTagName("item"))
                {
                    XmlNodeList elList = itemNode.GetElementsByTagName("key");
                    if (elList.Count == 1 && elList[0].InnerText.Contains("ellipsoid_radius"))
                    {
                        EllipsoidRadius = double.Parse(itemNode.GetElementsByTagName("value")[0].InnerText);
                    }
                }
            }
            if(!EllipsoidRadius.HasValue)
            {
                throw new Exception("Unexpected Places result, ellipsoid radius was null");
            }
            return EllipsoidRadius.Value;
        }

        /// <summary>
        /// Finds the estimated mars lat and lon for a given site drive
        /// </summary>
        /// <param name="sd"></param>
        /// <returns></returns>
        public Vector2 GetEstimatedLatLon(SiteDrive sd)
        {
            var config = PlacesConfig.Instance;
            string urlForRequest = string.Format("{0}/places/query/primary/{1}?from=rover({2},{3})&to=orbital(0)", config.Venue, config.View, sd.Site, sd.Drive);
            XmlDocument document = GetXmlDoc(urlForRequest);
            Vector3 v = ReadOffsetFromDocument(document);
            // x is northing
            // y is easting
            double ellipsoid_radius = GetElipsoidRadius();

            double lat = MathHelper.ToDegrees(v.X / ellipsoid_radius);
            double lon = MathHelper.ToDegrees(v.Y / ellipsoid_radius);
            return new Vector2(lat, lon);
        }

        /// <summary>
        /// Returns the ROVER frame offset between the "from" sitedrive to the "to" sitedrive
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public Vector3 GetEsitmatedOffset(SiteDrive from, SiteDrive to)
        {
            var config = PlacesConfig.Instance;
            string urlForRequest = string.Format("{0}/places/query/primary/{1}?from=rover({2},{3})&to=rover({4},{5})", config.Venue, config.View, from.Site, from.Drive, to.Site, to.Drive);
            XmlDocument document = GetXmlDoc(urlForRequest);
            return ReadOffsetFromDocument(document);
        }

        /// <summary>
        /// Finds the offset from the landing site to the current site drive
        /// </summary>
        /// <param name="sd"></param>
        /// <returns></returns>
        public Vector3 GetEsitmatedOffsetFromStart(SiteDrive sd)
        {
            return GetEsitmatedOffset(sd, new SiteDrive(1, 0));
        }
    }
}
