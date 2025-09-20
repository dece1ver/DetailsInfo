using MaterialDesignThemes.Wpf;
using System;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Windows;
using static DetailsInfo.Data.FileFormats;

namespace DetailsInfo
{
    public struct ArchiveContent
    {
        public string Name { get; set; }
        public readonly string Description => GetFileInfo(Name);
        public Visibility TransferButtonState { get; set; }
        public Visibility OpenButtonState { get; set; }
        public Visibility OpenFolderState { get; set; }
        public Visibility DeleteButtonState { get; set; }
        public Visibility AnalyzeButtonState { get; set; }
        public Visibility ShowWinExplorerButtonState { get; set; }
        public readonly bool CanBeTransfered => !NonTransferableExtensions.Contains(Path.GetExtension(Name).ToLower());
        public readonly PackIcon Icon => SetIcon(Name);

        public static string GetFileInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";

            try
            {
                var fi = new FileInfo(path);

                // Получаем владельца
                string owner = "неизвестен";
                try
                {
                    var identity = fi.GetAccessControl().GetOwner(typeof(NTAccount))?.ToString() ?? "неизвестен";

                    if (identity.Contains('\\'))
                    {
                        var parts = identity.Split('\\');
                        string domain = parts[0];
                        string login = parts[1];

                        if (domain.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                        {
                            owner = GetLocalUserFullName(login);
                        }
                        else
                        {
                            owner = GetDomainUserDisplayName(domain, login);
                        }

                        if (string.IsNullOrWhiteSpace(owner))
                            owner = login;
                    }
                    else
                    {
                        owner = identity;
                    }
                }
                catch { }

                string created = fi.CreationTime.ToString("dd.MM.yy HH:mm");
                string modified = fi.LastWriteTime.ToString("dd.MM.yy HH:mm");

                return $"✚ {owner}@{created} |» ✏️ {modified}";
            }
            catch
            {
                return "";
            }
        }

        private static string GetLocalUserFullName(string login)
        {
            try
            {
                string query = $"SELECT FullName FROM Win32_UserAccount WHERE Name='{login}' AND LocalAccount=True";
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                {
                    var fullName = obj["FullName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(fullName))
                        return fullName;
                }
            }
            catch { }
            return login;
        }

        private static string GetDomainUserDisplayName(string domain, string login)
        {
            try
            {
                string ldapPath = $"LDAP://{domain}";
                using var entry = new DirectoryEntry(ldapPath);
                using var searcher = new DirectorySearcher(entry);
                searcher.Filter = $"(&(objectClass=user)(sAMAccountName={login}))";
                searcher.PropertiesToLoad.Add("displayName");

                var result = searcher.FindOne();
                if (result != null && result.Properties.Contains("displayName"))
                    return result.Properties["displayName"][0]?.ToString();
            }
            catch { }
            return login;
        }
    }
}
