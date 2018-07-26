using Newtonsoft.Json;

namespace WeTransferUploader
{
    /// <summary>
    /// The base class for the responses from the web API.
    /// </summary>
    public abstract class JsonResponse
    {
        [JsonProperty("success")]
        public bool? Success { get; set; }

        public string RequestUri { get; set; }
    }

    public class TokenResponse : JsonResponse
    {
        [JsonProperty("token")]
        public string Token { get; set; }

    }

    public class CreateTransferResponse : JsonResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("version_identifier")]
        public string VersionIdentifier { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("shortened_url")]
        public string ShortenedUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("size")]
        public string Size { get; set; }

        [JsonProperty("total_items")]
        public string TotalItems { get; set; }

        [JsonProperty("items")]
        public object[] Items { get; set; }
    }

    public class AddItemResponse : JsonResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("content_identifier")]
        public string ContentIdentifier { get; set; }

        [JsonProperty("local_identifier")]
        public string LocalIdentifier { get; set; }
        
        [JsonProperty("meta")]
        public MultiPartItemDescription Meta { get; set; }

    }

    public class PartialUploadResponse : JsonResponse
    {
        [JsonProperty("upload_url")]
        public string UploadUrl { get; set; }

        [JsonProperty("part_number")]
        public int PartNumber { get; set; }

        [JsonProperty("upload_id")]
        public string UploadId { get; set; }

        [JsonProperty("upload_expires_at")]
        public long UploadExpiresAt { get; set; }

    }

    public class CompleteResponse : JsonResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }

}
