using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class PDSProductID
    {
        public string filename = null,
                         camera = null,
                         config = null,
                         clock = null,
                         framesize = null,
                         product = null,
                         site = null,
                         drive = null,
                         seqnum = null,
                         ver = null;

        public static PDSProductID ParseFromString(string productId)
        {
            // MIPL pattern
            string miplPattern = @"^(NL|FL|FR|RL|RR|ML|MR|NR|MH)([AB01234567RGBFULDCA_])[_TA-SU-Z](\d{9})([A-Z]{3}[LR_]|[A-Z]{4})(S|F|T|D|M)(\d{3})(\d{4})([A-Z_]{4})([0-9A-Z_]{5})M([1-9A-Z_]+)$";
            Match match = Regex.Match(productId, miplPattern);
            if (match.Success)
            {
                PDSProductID id = new PDSProductID();
                id.filename = productId;
                id.camera = match.Groups[1].Value;
                id.config = match.Groups[2].Value;
                id.clock = match.Groups[3].Value;
                id.product = match.Groups[4].Value.Replace("_", "");
                id.framesize = match.Groups[5].Value;
                id.site = match.Groups[6].Value;
                id.drive = match.Groups[7].Value;
                id.seqnum = match.Groups[9].Value;
                id.ver = match.Groups[10].Value;
                return id;
            }
            string mailinPattern = @"^(\d{4})(ML|MR|MH)(\d{16})(E|I|C|D)(\d{2})_D([RCXL][RCXL][RCXL])$";
            match = Regex.Match(productId, mailinPattern);
            if (match.Success)
            {
                PDSProductID id = new PDSProductID();
                id.filename = productId;
                id.camera = match.Groups[2].Value;
                id.product = match.Groups[6].Value;
                return id;
            }
            return null;
        }
    }
}
