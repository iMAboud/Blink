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

using Newtonsoft.Json;
using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Web;

namespace ShareX.UploadersLib.FileUploaders
{
    public class GoogleDriveFileUploaderService : FileUploaderService
    {
        public override FileDestination EnumValue { get; } = FileDestination.GoogleDrive;

        public override bool CheckConfig(UploadersConfig config)
        {
            return OAuth2Info.CheckOAuth(config.GoogleDriveOAuth2Info);
        }

        public override GenericUploader CreateUploader(UploadersConfig config, TaskReferenceHelper taskInfo)
        {
            return new GoogleDrive(config.GoogleDriveOAuth2Info)
            {
                IsPublic = config.GoogleDriveIsPublic,
                DirectLink = config.GoogleDriveDirectLink,
                FolderID = config.GoogleDriveUseFolder ? config.GoogleDriveFolderID : null,
                DriveID = config.GoogleDriveSelectedDrive?.id
            };
        }
    }

    public enum GoogleDrivePermissionRole
    {
        owner, reader, writer, organizer, commenter
    }

    public enum GoogleDrivePermissionType
    {
        user, group, domain, anyone
    }

    public sealed class GoogleDrive : FileUploader, IOAuth2
    {
        public GoogleOAuth2 OAuth2 { get; private set; }
        public OAuth2Info AuthInfo => OAuth2.AuthInfo;
        public bool IsPublic { get; set; }
        public bool DirectLink { get; set; }
        public string FolderID { get; set; }
        public string DriveID { get; set; }

        public static GoogleDriveSharedDrive MyDrive = new GoogleDriveSharedDrive
        {
            id = "", // empty defaults to user drive
            name = Localization.Strings.GoogleDrive_My_drive
        };

        public GoogleDrive(OAuth2Info oauth)
        {
            OAuth2 = new GoogleOAuth2(oauth, this)
            {
                Scope = "https://www.googleapis.com/auth/drive.appdata"
            };
        }

        public Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            return OAuth2.RefreshAccessTokenAsync(cancellationToken);
        }

        public Task<bool> CheckAuthorizationAsync(CancellationToken cancellationToken = default)
        {
            return OAuth2.CheckAuthorizationAsync(cancellationToken);
        }

        public Task<string> GetAuthorizationURLAsync(CancellationToken cancellationToken = default)
        {
            return OAuth2.GetAuthorizationURLAsync(cancellationToken);
        }

        public Task<bool> GetAccessTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            return OAuth2.GetAccessTokenAsync(code, cancellationToken);
        }

        private string GetMetadata(string name, string parentID, string driveID = "")
        {
            object metadata;

            // If there's no parent folder, the drive behaves as parent
            if (string.IsNullOrEmpty(parentID))
            {
                parentID = driveID;
            }

            if (!string.IsNullOrEmpty(parentID))
            {
                metadata = new
                {
                    name = name,
                    driveId = driveID,
                    parents = new[]
                    {
                        parentID
                    }
                };
            }
            else
            {
                metadata = new
                {
                    name = name
                };
            }

            return JsonConvert.SerializeObject(metadata);
        }

        private async Task SetPermissionsAsync(string fileID, GoogleDrivePermissionRole role, GoogleDrivePermissionType type,
            bool allowFileDiscovery, CancellationToken cancellationToken)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return;

            string url = string.Format("https://www.googleapis.com/drive/v3/files/{0}/permissions?supportsAllDrives=true", fileID);

            string json = JsonConvert.SerializeObject(new
            {
                role = role.ToString(),
                type = type.ToString(),
                allowFileDiscovery = allowFileDiscovery.ToString()
            });

            await SendRequestAsync(HttpMethod.POST, url, json, RequestHelpers.ContentTypeJSON, null, OAuth2.GetAuthHeaders(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<GoogleDriveFile>> GetFoldersAsync(string driveID = "", bool trashed = false, bool writer = true,
            CancellationToken cancellationToken = default)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return null;

            string query = "mimeType = 'application/vnd.google-apps.folder'";

            if (!trashed)
            {
                query += " and trashed = false";
            }

            if (writer && string.IsNullOrEmpty(driveID))
            {
                query += " and 'me' in writers";
            }

            Dictionary<string, string> args = new Dictionary<string, string>();
            args.Add("q", query);
            args.Add("fields", "nextPageToken,files(id,name,description)");
            if (!string.IsNullOrEmpty(driveID))
            {
                args.Add("driveId", driveID);
                args.Add("corpora", "drive");
                args.Add("supportsAllDrives", "true");
                args.Add("includeItemsFromAllDrives", "true");
            }

            List<GoogleDriveFile> folders = new List<GoogleDriveFile>();
            string pageToken = "";

            // Make sure we get all the pages of results
            do
            {
                args["pageToken"] = pageToken;
                string response = await SendRequestAsync(HttpMethod.GET, "https://www.googleapis.com/drive/v3/files", args, OAuth2.GetAuthHeaders(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                pageToken = "";

                if (!string.IsNullOrEmpty(response))
                {
                    GoogleDriveFileList fileList = JsonConvert.DeserializeObject<GoogleDriveFileList>(response);

                    if (fileList != null)
                    {
                        folders.AddRange(fileList.files);
                        pageToken = fileList.nextPageToken;
                    }
                }
            }
            while (!string.IsNullOrEmpty(pageToken));

            return folders;
        }

        public async Task<List<GoogleDriveSharedDrive>> GetDrivesAsync(CancellationToken cancellationToken = default)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return null;

            Dictionary<string, string> args = new Dictionary<string, string>();
            List<GoogleDriveSharedDrive> drives = new List<GoogleDriveSharedDrive>();
            string pageToken = "";

            // Make sure we get all the pages of results
            do
            {
                args["pageToken"] = pageToken;
                string response = await SendRequestAsync(HttpMethod.GET, "https://www.googleapis.com/drive/v3/drives", args, OAuth2.GetAuthHeaders(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                pageToken = "";

                if (!string.IsNullOrEmpty(response))
                {
                    GoogleDriveSharedDriveList driveList = JsonConvert.DeserializeObject<GoogleDriveSharedDriveList>(response);

                    if (driveList != null)
                    {
                        drives.AddRange(driveList.drives);
                        pageToken = driveList.nextPageToken;
                    }
                }
            }
            while (!string.IsNullOrEmpty(pageToken));

            return drives;
        }

        protected override async Task<UploadResult> UploadCoreAsync(Stream stream, string fileName, CancellationToken cancellationToken)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return null;

            string metadata = GetMetadata(fileName, FolderID, DriveID);

            UploadResult result = await SendRequestFileAsync("https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink,webContentLink&supportsAllDrives=true",
                stream, fileName, "file", headers: OAuth2.GetAuthHeaders(), contentType: "multipart/related", relatedData: metadata,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(result.Response))
            {
                GoogleDriveFile upload = JsonConvert.DeserializeObject<GoogleDriveFile>(result.Response);

                if (upload != null)
                {
                    AllowReportProgress = false;

                    if (IsPublic)
                    {
                        await SetPermissionsAsync(upload.id, GoogleDrivePermissionRole.reader, GoogleDrivePermissionType.anyone, false,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (DirectLink)
                    {
                        Uri webContentLink = new Uri(upload.webContentLink);

                        string leftPart = webContentLink.GetLeftPart(UriPartial.Path);

                        NameValueCollection queryString = HttpUtility.ParseQueryString(webContentLink.Query);
                        queryString.Remove("export");

                        result.URL = $"{leftPart}?{queryString}";
                    }
                    else
                    {
                        result.URL = upload.webViewLink;
                    }
                }
            }

            return result;
        }

        public async Task<GoogleDriveFile> FindFileAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return null;

            Dictionary<string, string> args = new Dictionary<string, string>
            {
                { "q", $"name = '{fileName.Replace("'", "\\'")}' and trashed = false" },
                { "fields", "files(id,name,size,modifiedTime)" },
                { "spaces", "appDataFolder" }
            };

            string response = await SendRequestAsync(HttpMethod.GET, "https://www.googleapis.com/drive/v3/files", args, OAuth2.GetAuthHeaders(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(response))
            {
                GoogleDriveFileList fileList = JsonConvert.DeserializeObject<GoogleDriveFileList>(response);
                return fileList?.files?.FirstOrDefault();
            }

            return null;
        }

        public async Task<bool> DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken = default)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return false;

            HttpClient client = HttpClientFactory.Create(true);
            using HttpRequestMessage request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthInfo.Token.access_token);

            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;

            string dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using FileStream fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async Task<GoogleDriveFile> UploadBackupFileAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
        {
            if (!await CheckAuthorizationAsync(cancellationToken).ConfigureAwait(false)) return null;

            GoogleDriveFile existing = await FindFileAsync(fileName, cancellationToken).ConfigureAwait(false);

            HttpClient client = HttpClientFactory.Create(true);

            if (existing != null && !string.IsNullOrEmpty(existing.id))
            {
                // Update existing file content via PATCH
                using HttpRequestMessage patchRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Patch,
                    $"https://www.googleapis.com/upload/drive/v3/files/{existing.id}?uploadType=media");
                patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthInfo.Token.access_token);

                byte[] fileBytes = await ReadStreamBytesAsync(stream, cancellationToken).ConfigureAwait(false);
                patchRequest.Content = new ByteArrayContent(fileBytes);
                patchRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using HttpResponseMessage patchResponse = await client.SendAsync(patchRequest, cancellationToken).ConfigureAwait(false);
                if (patchResponse.IsSuccessStatusCode)
                {
                    string json = await patchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(json))
                    {
                        GoogleDriveFile updated = JsonConvert.DeserializeObject<GoogleDriveFile>(json);
                        if (updated != null) return updated;
                    }
                    return existing;
                }

                // PATCH failed — fall through to create
                stream = new MemoryStream(fileBytes);
            }

            // Create new file in appDataFolder via multipart/related
            string metadata = JsonConvert.SerializeObject(new { name = fileName, parents = new[] { "appDataFolder" } });
            string boundary = "backup_boundary_" + Guid.NewGuid().ToString("N");

            using HttpRequestMessage createRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,size,modifiedTime");
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthInfo.Token.access_token);

            MultipartContent multipart = new MultipartContent("related", boundary);
            ByteArrayContent metadataContent = new ByteArrayContent(Encoding.UTF8.GetBytes(metadata));
            metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=UTF-8");
            multipart.Add(metadataContent);

            byte[] createFileBytes = await ReadStreamBytesAsync(stream, cancellationToken).ConfigureAwait(false);
            ByteArrayContent fileContent = new ByteArrayContent(createFileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent);

            createRequest.Content = multipart;

            using HttpResponseMessage createResponse = await client.SendAsync(createRequest, cancellationToken).ConfigureAwait(false);
            if (createResponse.IsSuccessStatusCode)
            {
                string createJson = await createResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(createJson))
                {
                    return JsonConvert.DeserializeObject<GoogleDriveFile>(createJson);
                }
            }

            return null;
        }

        private static async Task<byte[]> ReadStreamBytesAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                return buffer.ToArray();
            }

            using MemoryStream copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            return copy.ToArray();
        }
    }

    public class GoogleDriveFile
    {
        public string id { get; set; }
        public string webViewLink { get; set; }
        public string webContentLink { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public long? size { get; set; }
        public DateTime? modifiedTime { get; set; }
    }

    public class GoogleDriveFileList
    {
        public List<GoogleDriveFile> files { get; set; }
        public string nextPageToken { get; set; }
    }

    public class GoogleDriveSharedDrive
    {
        public string id { get; set; }
        public string name { get; set; }

        public override string ToString()
        {
            return name;
        }
    }

    public class GoogleDriveSharedDriveList
    {
        public List<GoogleDriveSharedDrive> drives { get; set; }
        public string nextPageToken { get; set; }
    }
}
