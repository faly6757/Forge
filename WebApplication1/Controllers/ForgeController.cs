using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Autodesk.Forge.Model;
using Autodesk.Forge;
using WebApplication1.Models;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace WebApplication1.Controllers
{
    public class ForgeController : Controller
    {
        private static List<Document> _documents = new List<Document>();
        private const string BUCKET_KEY = "forge_bucket_001_";
        private const string FORGE_BASE_URL = "https://developer.api.autodesk.com/";
        private const string CLIENT_ID = "AUjiCigDAig9WgBL5tA20O7sXZjuJaev6Y9Qt0MizR3SCNIW";
        private const string CLIENT_SECRET = "ef3kIzIprgAYa6yXNwEWGtKBX7BWL08aWtxfH9G8tAbp8CBrxyGVKnPtrIh8Af85";

        public ForgeController()
        {
            // Ajout de données de test si la liste est vide
            if (_documents.Count == 0)
            {
                _documents.Add(new Document
                {
                    Id = "test1",
                    Name = "Plan_Architecture.pdf",
                    FileType = "application/pdf",
                    ForgeUrn = "dXJuOmFkc2sub2JqZWN0czpvcy5vYmplY3Q6Zm9yZ2VfYnVja2V0XzAwMV8vUGxhbl9BcmNoaXRlY3R1cmUucGRm",
                    UploadDate = DateTime.Now.AddDays(-2)
                });

                _documents.Add(new Document
                {
                    Id = "test2",
                    Name = "Structure_Batiment.pdf",
                    FileType = "application/pdf",
                    ForgeUrn = "dXJuOmFkc2sub2JqZWN0czpvcy5vYmplY3Q6Zm9yZ2VfYnVja2V0XzAwMV8vU3RydWN0dXJlX0JhdGltZW50LnBkZg",
                    UploadDate = DateTime.Now.AddDays(-1)
                });

                _documents.Add(new Document
                {
                    Id = "test3",
                    Name = "Installations_Electriques.pdf",
                    FileType = "application/pdf",
                    ForgeUrn = "dXJuOmFkc2sub2JqZWN0czpvcy5vYmplY3Q6Zm9yZ2VfYnVja2V0XzAwMV8vSW5zdGFsbGF0aW9uc19FbGVjdHJpcXVlcy5wZGY",
                    UploadDate = DateTime.Now
                });
            }
        }

        // GET: /Forge/Index
        public ActionResult Index()
        {
            return View(_documents);
        }

        // GET: /Forge/Upload
        public ActionResult Upload()
        {
            return View();
        }

        // POST: /Forge/Upload
        [HttpPost]
        public async Task<ActionResult> Upload(HttpPostedFileBase file)
        {
            if (file != null && file.ContentLength > 0)
            {
                try
                {
                    // 1. Authentification Forge
                    var credentials = await GetForgeCredentials();
                    string accessToken = credentials.access_token?.ToString(); 

                    // 2. Créer le bucket s'il n'existe pas
                    using (var client = new HttpClient())
                    {
                        client.BaseAddress = new Uri(FORGE_BASE_URL);
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);


                        var bucketPayload = new
                        {
                            bucketKey = BUCKET_KEY.ToLower(),
                            policyKey = "transient" 
                        };

                        var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(bucketPayload);
                        var bucketContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        // Appel pour créer le bucket
                        var bucketResponse = await client.PostAsync("/oss/v2/buckets", bucketContent);

                        if (!bucketResponse.IsSuccessStatusCode)
                        {
                            // Le code 409 (Conflict) indique que le bucket existe déjà – on peut l'ignorer
                            if (bucketResponse.StatusCode != System.Net.HttpStatusCode.Conflict)
                            {
                                var error = await bucketResponse.Content.ReadAsStringAsync();
                                throw new Exception($"Erreur création bucket : {bucketResponse.StatusCode} - {error}");
                            }
                        }

                        // 3. Upload du fichier
                        using (var memoryStream = new MemoryStream())
                        {
                            file.InputStream.CopyTo(memoryStream);
                            memoryStream.Position = 0;

                            var content = new StreamContent(memoryStream);
                            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

                            var uploadResponse = await client.PutAsync(
                                $"/oss/v2/buckets/{BUCKET_KEY.ToLower()}/objects/{file.FileName}",
                                content
                            );

                            if (!uploadResponse.IsSuccessStatusCode)
                            {
                                var error = await uploadResponse.Content.ReadAsStringAsync();
                                throw new Exception($"Erreur Upload : {uploadResponse.StatusCode} - {error}");
                            }

                            var uploadResult = await uploadResponse.Content.ReadAsStringAsync();
                            dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(uploadResult);
                            string objectId = result.objectId;

                            // 4. Lancer la conversion si c'est un PDF
                            string urn = null;
                            if (file.ContentType == "application/pdf")
                            {
                                urn = Base64Encode(objectId);
                                var derivativesApi = new DerivativesApi();
                                derivativesApi.Configuration.AccessToken = accessToken;

                                var job = new JobPayload(
                                    new JobPayloadInput(urn),
                                    new JobPayloadOutput(
                                        new List<JobPayloadItem> { new JobPayloadItem(JobPayloadItem.TypeEnum.Svf, new List<JobPayloadItem.ViewsEnum> { JobPayloadItem.ViewsEnum._2d, JobPayloadItem.ViewsEnum._3d }) }
                                    )
                                );
                                await derivativesApi.TranslateAsync(job, true);
                            }

                            // 5. Ajouter le document à la liste
                            var document = new Document
                            {
                                Id = objectId,
                                Name = file.FileName,
                                FileType = file.ContentType,
                                ForgeUrn = urn,
                                UploadDate = DateTime.Now
                            };
                            _documents.Add(document);

                            return RedirectToAction("Index");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Erreur lors de l'upload : " + ex.Message);
                    return View();
                }
            }

            return View();
        }

        // GET: /Forge/Viewer/{id}
        public async Task<ActionResult> Viewer(string id)
        {
            var document = _documents.FirstOrDefault(d => d.Id == id);
            if (document == null)
                return HttpNotFound();

            var credentials = await GetForgeCredentials();
            ViewBag.URN = document.ForgeUrn;
            ViewBag.AccessToken = credentials.access_token;
            ViewBag.DocumentName = document.Name;
            return View();
        }

        // GET: /Forge/GetViewerData/{id}
        public async Task<JsonResult> GetViewerData(string id)
        {
            var document = _documents.FirstOrDefault(d => d.Id == id);
            if (document == null)
                return Json(new { error = "Document non trouvé" }, JsonRequestBehavior.AllowGet);

            var credentials = await GetForgeCredentials();
            return Json(new
            {
                urn = document.ForgeUrn,
                accessToken = credentials.access_token
            }, JsonRequestBehavior.AllowGet);
        }

        // GET: /Forge/GetObservations?documentId=xxx
    /*    public async Task<JsonResult> GetObservations(string documentId)
        {
           
        }*/

        private long GetFileIdFromDocumentId(string documentId)
        {
            // Ici, tu fais le mapping documentId -> FileId
            if (documentId == "test1") return 123;  
            if (documentId == "test2") return 456;
            return 0;  // Si pas trouvé
        }

        private long GetRevisionFromDocumentId(string documentId)
        {
            // Si révision = même ID pour l'exemple, sinon adapte
            return GetFileIdFromDocumentId(documentId);
        }

        private async Task<dynamic> GetForgeCredentials()
        {
            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", CLIENT_ID),
                    new KeyValuePair<string, string>("client_secret", CLIENT_SECRET),
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", "data:read data:write data:create bucket:create bucket:read viewables:read")
                });

                var response = await client.PostAsync("https://developer.api.autodesk.com/authentication/v2/authenticate", content);

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                return Newtonsoft.Json.JsonConvert.DeserializeObject(result);
            }
            
        }

        private string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes).TrimEnd('=');
        }
    }
}