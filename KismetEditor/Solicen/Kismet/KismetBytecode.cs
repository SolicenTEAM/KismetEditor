using Newtonsoft.Json.Linq;
using Solicen.Kismet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UAssetAPI;

namespace KismetEditor.Solicen
{
    internal class KismetBytecode
    {
        internal const int MAGIC_TES = 200519; // | 20 | 05 | 19 | To   End Script 
        internal const int MAGIC_FES = 060519; // | 06 | 05 | 19 | From End Script

        public int ModifiedInst = 0;
        public JObject JsonObject;
        public string Json => JsonObject.ToString();
        public KismetBytecode(JObject json)
        {
            JsonObject = json;
        }
    }
}
