using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DetailsInfo.Data
{
    public class UsbDrive
    {
        public UsbDrive(DriveInfo drive, string description)
        {
            Drive = drive;
            Description = description;
        }

        public DriveInfo Drive { get; set; }
        public string Description { get; set; }
    }
}
