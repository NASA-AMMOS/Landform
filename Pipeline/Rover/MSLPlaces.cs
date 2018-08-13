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
        [ConfigEnvironmentVariable("PLACES_USERNAME")]
        public string Username { get; set; }

        [ConfigEnvironmentVariable("PLACES_API_KEY")]
        public string APIKey { get; set; }

        protected override string ConfigFilename()
        {
            return "places";
        }
    }

    public class MSLPlaces
    {
        const string VENUE = "msl-ops";
        const string VIEW = "localized_interp";    // options: best_tactical, localized_pos, localized_interp 
        string URLRoot = @"https://mslplaces.jpl.nasa.gov:9443";  // TODO: make into a conifg variable
        double? EllipsoidRadius = null;

        XmlDocument GetXmlDoc(string url)
        {
            RestClient client = new RestClient();
            client.BaseUrl = new Uri(URLRoot);
            var config = PlacesConfig.Instance;
            client.Authenticator = new HttpBasicAuthenticator(config.Username, config.APIKey);
            var request = new RestRequest();
            request.Resource = url;
            IRestResponse response = client.Execute(request);
            XmlDocument document = new XmlDocument();
            document.LoadXml(response.Content);
            return document;
        }

        Vector3 ReadOffsetFromDocument(XmlDocument doc)
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

        double GetElipsoidRadius()
        {
            if (EllipsoidRadius == null)
            {
                string urlForRequest = string.Format(@"{0}/places/rmc/orbital(0)/metadata", VENUE);
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
            return EllipsoidRadius.Value;
        }

        public Vector2 GetEstimatedLatLon(SiteDrive sd)
        {
            string urlForRequest = string.Format(@"{0}/places/query/primary/{1}?from=rover({2},{3})&to=orbital(0)", VENUE, VIEW, sd.Site, sd.Drive);
            XmlDocument document = GetXmlDoc(urlForRequest);
            Vector3 v = ReadOffsetFromDocument(document);
            // x is northing
            // y is easting
            double ellipsoid_radius = GetElipsoidRadius();
            double lat = (v.X / ellipsoid_radius) * (180 / Math.PI);
            double lon = (v.Y / ellipsoid_radius) * (180 / Math.PI);
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
            string urlForRequest = string.Format(@"{0}/places/query/primary/{1}?from=rover({2},{3})&to=rover({4},{5})", VENUE, VIEW, from.Site, from.Drive, to.Site, to.Drive);
            XmlDocument document = GetXmlDoc(urlForRequest);
            return ReadOffsetFromDocument(document);
        }

        /// <summary>
        /// Returns the estimated offset from the supplied site drive to the starting site drive
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public Vector3 GetEsitmatedOffsetFromStart(SiteDrive sd)
        {
            string urlForRequest = string.Format(@"{0}/places/query/primary/{1}?from=rover({2},{3})&to=rover(1,0)", VENUE, VIEW, sd.Site, sd.Drive);
            XmlDocument document = GetXmlDoc(urlForRequest);
            return ReadOffsetFromDocument(document);
        }
    }
}
