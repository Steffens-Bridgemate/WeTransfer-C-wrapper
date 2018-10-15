using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    public abstract class WeTransferCommunicator
    {
        //Clears the token
        public static void ClearToken()
        {
            Properties.Settings.Default.TokenV2 = null;
        }

        /// <summary>
        /// Returns a new token. A token is valid for a year.
        /// </summary>
        /// <returns></returns>
        public async Task<TokenResponseV2> GetToken(string user)
        {
            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException(nameof(user));

            using (var client = new HttpClient())
            {
                var request = new TokenRequest(ApiKey, new TokenRequestContent(user));

                var tokenResponse = await WaitForResponse<TokenResponseV2>(client, request);
                if (tokenResponse.Success.Value)
                {
                    Token = tokenResponse.Token;
                }
                return tokenResponse;
            }

        }


        /// <summary>
        /// A Json web token (JWT) that allows creation of transfer objects for a year. The secret API key is needed to obtain one.
        /// The token is stored in de project's properties, together with its creation date.
        /// </summary>
        public static string Token
        {
            get
            {
                var ageOfCurrentToken = DateTime.Now.Subtract(Properties.Settings.Default.TokenV2CreationDate);
                if (ageOfCurrentToken > TimeSpan.FromDays(300))
                {
                    //Logger.Debug("Token is too old and will not be returned.");
                    return null;
                }

                else
                {
                    return Properties.Settings.Default.TokenV2;
                }

            }
            set
            {
                //Logger.Debug($"Token set to: {value}.");
                Properties.Settings.Default.TokenV2 = value;
                Properties.Settings.Default.TokenV2CreationDate = DateTime.Now;
                Properties.Settings.Default.Save();
            }
        }


        // The secret, developer specific, API-key.
        protected string ApiKey { get; set; }

        //The directory where chunks of a file can be created.
        protected string ChunkDirectory { get; set; }

        #region "Helper methods"

        /// <summary>
        /// Performs the actual upload of one chunk of a file.
        /// </summary>
        /// <param name="requestUri">The url where the file must be uploaded to.</param>
        /// <param name="fullPath">The full path to the file</param>
        /// <returns></returns>
        protected async Task<PartialUploadResponse> UploadPart(string requestUri, string fullPath)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                HttpResponseMessage response;
                using (var stream = File.OpenRead(fullPath))
                {
                    request.Content = new StreamContent(stream);
                    response = await client.SendAsync(request);
                }

                var uploadResponse = new PartialUploadResponse { Success = response.StatusCode == System.Net.HttpStatusCode.OK };

                return uploadResponse;
            }

        }


        /// <summary>
        /// Sends the HttpRequestMessage and awaits its response containing a JSON array.
        /// </summary>
        /// <typeparam name="TResponse">The type of response that is expected.</typeparam>
        /// <param name="client">The HttpClient</param>
        /// <param name="request">The HttpRequestMessage</param>
        /// <returns></returns>
        protected async Task<(HttpStatusCode statusCode, string statusMessage, TResponse[] responseArray)>
            WaitForResponseArray<TResponse>(HttpClient client,
                                            HttpRequestMessage request) where TResponse : JsonResponseV2
        {
            var response = await client.SendAsync(request);
            var result = response.StatusCode;
            var message = response.ReasonPhrase;
            var content = await response.Content.ReadAsStringAsync();

            var responses = JsonConvert.DeserializeObject<TResponse[]>(content);

            return (statusCode: result, statusMessage: message, responseArray: responses);
        }

        /// <summary>
        /// Sends the HttpRequestMessage and awaits its response.
        /// </summary>
        /// <typeparam name="TResponse">The type of response that is expected.</typeparam>
        /// <param name="client">The HttpClient</param>
        /// <param name="request">The HttpRequestMessage</param>
        /// <returns></returns>
        protected async Task<TResponse> WaitForResponse<TResponse>(HttpClient client, HttpRequestMessage request) where TResponse : JsonResponseV2
        {

            var response = await client.SendAsync(request);
            var result = response.StatusCode;
            var message = await response.Content.ReadAsStringAsync();

            var tResponse = JsonConvert.DeserializeObject<TResponse>(message);
            tResponse.RequestUri = request.RequestUri.ToString();
            tResponse.Message = response.ReasonPhrase;

            if (tResponse.Success == null)
                tResponse.Success = result.HasFlag(HttpStatusCode.OK);
            return tResponse;
        }
        #endregion
    }

}
