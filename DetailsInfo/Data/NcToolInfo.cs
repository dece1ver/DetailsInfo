using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetailsInfo.Data
{
    public partial struct NcToolInfo
    {
        public int Position { get; set; }
        public string Comment { get; set; }
        public int LengthCompensation { get; set; }
        public int RadiusCompensation { get; set; }
        public Coolant Coolant { get; set;}
        public int Line { get; set;}
    }
}
