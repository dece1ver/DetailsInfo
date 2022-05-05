using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;

namespace DetailsInfo.Utils
{
    public static class Util
    {
        public static SolidColorBrush BrushFromHex(string hexColorString)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFrom(hexColorString);
        }

        public static string Encrypt(string plainText, string password,
        string salt = "dece1ver", string hashAlgorithm = "SHA1",
        int passwordIterations = 2, string initialVector = "OFRna73m*aze01xY",
        int keySize = 256)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return "";
            }

            byte[] initialVectorBytes = Encoding.ASCII.GetBytes(initialVector);
            byte[] saltValueBytes = Encoding.ASCII.GetBytes(salt);
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            PasswordDeriveBytes derivedPassword = new PasswordDeriveBytes(password, saltValueBytes, hashAlgorithm, passwordIterations);
            byte[] keyBytes = derivedPassword.GetBytes(keySize / 8);
            AesCng symmetricKey = new();
            symmetricKey.Mode = CipherMode.CFB;

            byte[] cipherTextBytes = null;

            using (ICryptoTransform encryptor = symmetricKey.CreateEncryptor(keyBytes, initialVectorBytes))
            {
                using (MemoryStream memStream = new MemoryStream())
                {
                    using (CryptoStream cryptoStream = new CryptoStream(memStream, encryptor, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                        cryptoStream.FlushFinalBlock();
                        cipherTextBytes = memStream.ToArray();
                        memStream.Close();
                        cryptoStream.Close();
                    }
                }
            }

            symmetricKey.Clear();
            return Convert.ToBase64String(cipherTextBytes);
        }

        public static string Decrypt(string cipherText, string password,
        string salt = "dece1ver", string hashAlgorithm = "SHA1",
        int passwordIterations = 2, string initialVector = "OFRna73m*aze01xY",
        int keySize = 256)
        {
            if (string.IsNullOrEmpty(cipherText))
            {
                return "";
            }

            byte[] initialVectorBytes = Encoding.ASCII.GetBytes(initialVector);
            byte[] saltValueBytes = Encoding.ASCII.GetBytes(salt);
            byte[] cipherTextBytes = Convert.FromBase64String(cipherText);

            PasswordDeriveBytes derivedPassword = new PasswordDeriveBytes(password, saltValueBytes, hashAlgorithm, passwordIterations);
            byte[] keyBytes = derivedPassword.GetBytes(keySize / 8);

            AesCng symmetricKey = new();
            symmetricKey.Mode = CipherMode.CFB;

            byte[] plainTextBytes = new byte[cipherTextBytes.Length];
            int byteCount = 0;

            using (ICryptoTransform decryptor = symmetricKey.CreateDecryptor(keyBytes, initialVectorBytes))
            {
                using (MemoryStream memStream = new MemoryStream(cipherTextBytes))
                {
                    using (CryptoStream cryptoStream = new CryptoStream(memStream, decryptor, CryptoStreamMode.Read))
                    {
                        byteCount = cryptoStream.Read(plainTextBytes);
                        memStream.Close();
                        cryptoStream.Close();
                    }
                }
            }

            symmetricKey.Clear();
            return Encoding.UTF8.GetString(plainTextBytes, 0, byteCount);
        }
    }
}
