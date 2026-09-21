#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

#nullable enable

namespace ShareX.UploadersLib
{
    internal static partial class APIKeys
    {
        // Image uploaders
        public static readonly string ImgurClientID = "";
        public static readonly string ImgurClientSecret = "";
        public static readonly string ImageShackKey = "";
        public static readonly string FlickrKey = "";
        public static readonly string FlickrSecret = "";
        public static readonly string PhotobucketConsumerKey = "";
        public static readonly string PhotobucketConsumerSecret = "";

        // Text uploaders
        public static readonly string PastebinKey = "";
        public static readonly string GitHubID = "";
        public static readonly string GitHubSecret = "";
        public static readonly string Paste_eeApplicationKey = "";

        // File uploaders
        public static readonly string DropboxConsumerKey = "";
        public static readonly string DropboxConsumerSecret = "";
        public static readonly string BoxClientID = "";
        public static readonly string BoxClientSecret = "";
        public static readonly string SendSpaceKey = "";
        public static readonly string MediaFireAppId = "";
        public static readonly string MediaFireApiKey = "";
        public static readonly string OneDriveClientID = "";
        public static readonly string OneDriveClientSecret = "";

        // URL shorteners
        public static readonly string BitlyClientID = "";
        public static readonly string BitlyClientSecret = "";

        // Other services
        private static string? _googleClientId;
        public static string GoogleClientID => _googleClientId ??= DecryptGoogleCredential(GoogleClientIdEncrypted);

        private static string? _googleClientSecret;
        public static string GoogleClientSecret => _googleClientSecret ??= DecryptGoogleCredential(GoogleClientSecretEncrypted);

        private static readonly byte[] GoogleKeyMask = [111, 250, 199, 34, 26, 18, 164, 183, 118, 149, 235, 34, 25, 80, 60, 123, 250, 31, 200, 58, 172, 105, 190, 211, 147, 49, 18, 117, 29, 105, 162, 172];
        private static readonly byte[] GoogleObfKey = [59, 176, 128, 61, 212, 50, 12, 138, 229, 180, 112, 71, 80, 228, 48, 253, 66, 248, 234, 134, 166, 123, 52, 173, 61, 51, 59, 141, 158, 245, 194, 12];
        private static readonly byte[] GoogleIvMask = [198, 146, 148, 0, 199, 73, 103, 219, 62, 139, 156, 187, 159, 157, 105, 75];
        private static readonly byte[] GoogleObfIv = [176, 78, 255, 86, 160, 146, 106, 145, 139, 51, 204, 32, 97, 21, 219, 126];
        private static readonly byte[] GoogleClientIdEncrypted = [227, 70, 215, 236, 123, 130, 58, 203, 76, 204, 119, 1, 232, 253, 118, 230, 185, 84, 157, 57, 85, 15, 239, 171, 55, 153, 81, 223, 184, 134, 23, 108, 69, 217, 250, 207, 92, 238, 179, 37, 111, 26, 192, 187, 7, 160, 103, 201, 178, 225, 70, 215, 184, 108, 104, 97, 232, 47, 43, 125, 251, 49, 229, 240, 124, 233, 188, 231, 224, 157, 157, 23, 238, 82, 156, 4, 248, 69, 171, 87];
        private static readonly byte[] GoogleClientSecretEncrypted = [133, 69, 105, 98, 227, 141, 42, 10, 101, 0, 162, 225, 209, 210, 27, 194, 36, 179, 230, 236, 159, 192, 148, 44, 93, 33, 230, 124, 22, 62, 189, 213, 185, 127, 160, 132, 165, 176, 221, 214, 191, 172, 62, 198, 54, 138, 131, 145];

        private static string DecryptGoogleCredential(byte[] cipherBytes)
        {
            try
            {
                byte[] key = new byte[32];
                for (int i = 0; i < 32; i++)
                {
                    key[i] = (byte)(GoogleKeyMask[i] ^ GoogleObfKey[i]);
                }

                byte[] iv = new byte[16];
                for (int i = 0; i < 16; i++)
                {
                    iv[i] = (byte)(GoogleIvMask[i] ^ GoogleObfIv[i]);
                }

                using System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                using var ms = new System.IO.MemoryStream(cipherBytes, 0, cipherBytes.Length);
                using var cs = new System.Security.Cryptography.CryptoStream(ms, decryptor, System.Security.Cryptography.CryptoStreamMode.Read);
                using var reader = new System.IO.StreamReader(cs, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}