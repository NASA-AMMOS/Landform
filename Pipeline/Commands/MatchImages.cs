using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{

    [Verb("matchimages", HelpText = "")]
    public class MatchImagesOptions
    {
        [Value(0, Required = true, HelpText = "")]
        public string ImageA { get; set; }

        [Value(1, Required = true, HelpText = "")]
        public string ImageB { get; set; }

    }

    public class MatchImages
    {
        public MatchImagesOptions options;
        public MatchImages(MatchImagesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            return 0;
        }
    }
}
