namespace DetailsInfo.Data
{
    public struct UserConfig
    {
        public string TablePath { get; set; }
        public int FileEncoding { get; set; }
        public string ArchivePath { get; set; }
        public string MachinePath { get; set; }
        public string TempPath { get; set; }
        public int RefreshInterval { get; set; }
        public bool AutoRename { get; set; }
        public string NetLogPath { get; set; }
        public string EmailLogin { get; set; }
        public string EmailPass { get; set; }
        public string PopServer { get; set; }
        public int PopPort { get; set; }
        public bool UseSsl { get; set; }
    }
}
