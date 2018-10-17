using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    /// <summary>
    /// Creates the fixed part of the HttpRequestMessage
    /// </summary>
    /// <param name="requestUri">The url that the request must be sent to.</param>
    /// <param name="method">The HttpMethod: Post, Get, Put</param>
    /// <returns></returns>
    internal abstract class WeTransferHttpRequestMessage<TContent> : HttpRequestMessage where TContent : class
    {
        //An ugly hack: HttpClient and HttpRequestMessage do not allow the Content-Type header on Get calls as .Net does not support sending of content with Get calls.
        //This code hacks the base classes to remove this restriction on the Content-Type header.
        //Many thanks to: https://stackoverflow.com/questions/10679214/how-do-you-set-the-content-type-header-for-an-httpclient-request/41231353#41231353
        public static void AllowContentTypeHeaderOnGetCalls()
        {
            var field = typeof(System.Net.Http.Headers.HttpRequestHeaders)
                                 .GetField("invalidHeaders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                                            ?? typeof(System.Net.Http.Headers.HttpRequestHeaders)
                                 .GetField("s_invalidHeaders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field != null)
            {
                var invalidFields = (HashSet<string>)field.GetValue(null);
                invalidFields.Remove(ContentType);
            }
        }
        public const string JsonMediaType = "application/json";
        public const string ContentType = "Content-Type";
        protected WeTransferHttpRequestMessage(string requestUri,
                                             HttpMethod method,
                                             TContent content)
        {

            base.Method = method;
            base.RequestUri = new Uri(requestUri);

            //This safeguard is no longer necessary.
            //if (method == HttpMethod.Get )
            //{
            //    AllowContentTypeHeaderOnGetCalls();
            //    base.Headers.TryAddWithoutValidation(ContentType, JsonMediaType);
            //}
            //else
                base.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(JsonMediaType));

           
            if (content != null)
            {
                base.Content = new StringContent(JsonConvert.SerializeObject(content), Encoding.UTF8, JsonMediaType);
            }
        }

        /// <summary>
        /// Adds dummy content in order to ensure that a Content-Type header is added.
        /// </summary>
        protected void AddDummyData()
        {
            base.Content = new StringContent("", Encoding.UTF8, JsonMediaType);
        }
    }

    internal class ApiKeyedWeTransferHttpRequestMessage<TContent> : WeTransferHttpRequestMessage<TContent> where TContent : class
    {
        public ApiKeyedWeTransferHttpRequestMessage(string requestUri,
                                                    HttpMethod method,
                                                    string apiKey,
                                                    TContent content) : base(requestUri, method, content)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            base.Headers.Add("x-api-key", apiKey);
        }
    }

    internal class TokenizedWeTransferHttpRequestMessage<TContent> : ApiKeyedWeTransferHttpRequestMessage<TContent> where TContent : class
    {
        public TokenizedWeTransferHttpRequestMessage(string requestUri,
                                                     HttpMethod method,
                                                     string apiKey,
                                                     string token,
                                                     TContent content) : base(requestUri, method, apiKey, content)
        {
            if (string.IsNullOrEmpty(token))
                throw new ArgumentNullException(nameof(token));

            base.Headers.Add("Authorization", $"Bearer {token}");
        }

    }

    internal class TokenRequest : ApiKeyedWeTransferHttpRequestMessage<TokenRequestContent>
    {
        public TokenRequest(string apiKey, TokenRequestContent content) : base(RequestUrisV2.Token, HttpMethod.Post, apiKey, content)
        { }
    }

    internal class CreateTransferRequestV2 : TokenizedWeTransferHttpRequestMessage<FileUploadRequestContent>
    {
        public CreateTransferRequestV2(string apiKey,
                                       string token,
                                       FileUploadRequestContent content):base(RequestUrisV2.CreateTransfer,
                                                                       HttpMethod.Post,
                                                                       apiKey,
                                                                       token,
                                                                       content)
        { }
    }

    internal class FilePartUploadUrlRequestV2: TokenizedWeTransferHttpRequestMessage<object>
    {
        public FilePartUploadUrlRequestV2(string apiKey,
                                          string token,
                                          string transferId,
                                          string fileId,
                                          int partNumber):base(string.Format(RequestUrisV2.FilePartUploadUrl,transferId,fileId,partNumber),
                                                               HttpMethod.Get,
                                                               apiKey,
                                                               token,
                                                               content:null)
        {}
    }

    internal class SingleFileUploadCompleteRequestV2:TokenizedWeTransferHttpRequestMessage<SingleFileCompletedRequestContent>
    {
        public SingleFileUploadCompleteRequestV2(string apiKey,
                                                 string token,
                                                 string transferId,
                                                 string fileId,
                                                 int numberOfParts):base(string.Format(RequestUrisV2.FileUploadComplete,transferId,fileId),
                                                                     HttpMethod.Put,
                                                                     apiKey,
                                                                     token,
                                                                     content:new SingleFileCompletedRequestContent(numberOfParts))
        { }
    }

    internal class TransferCompleteRequestV2 : TokenizedWeTransferHttpRequestMessage<object>
    {
        public TransferCompleteRequestV2(string apiKey,
                                         string token,
                                         string transferId) :base(string.Format(RequestUrisV2.TransferComplete, transferId),
                                                                  HttpMethod.Put,
                                                                  apiKey,
                                                                  token,
                                                                  content: null)
           
        {
            //In order to get the Content-Type header added to the request some dummy content must be created.
            AddDummyData();
        }
    }

    #region "BoardApiSpecificRequests"
    internal class CreateBoardRequest: TokenizedWeTransferHttpRequestMessage<BoardCreationRequestContent>
    {
        public CreateBoardRequest(string apiKey,
                                  string token,
                                  string name,
                                  string description=null):
            base(RequestUrisV2.CreateBoard,
            HttpMethod.Post,
            apiKey,token,
            new BoardCreationRequestContent(name,description))
        { }
    }

    internal class GetBoardInfoRequest: TokenizedWeTransferHttpRequestMessage<object>
    {
        public GetBoardInfoRequest(string apiKey,
                                   string token,
                                   string boardId) :
           base(string.Format( RequestUrisV2.GetBoardInfo,boardId),
           HttpMethod.Get,
           apiKey, 
           token,
           content:null)
        { }
    }

    internal class AddLinksRequest:TokenizedWeTransferHttpRequestMessage<LinkRequestContent[]>
    {
        public AddLinksRequest(string apiKey,
                               string token,
                               string boardId,
                               List<(string url,string title)> links):
            base(string.Format( RequestUrisV2.AddLinks,boardId),
                 HttpMethod.Post,
                 apiKey,
                 token,
                 (new AddLinksRequestContent(links)).Links)
        { }
    }

    internal class AddFilesRequest : TokenizedWeTransferHttpRequestMessage<FileRequestContent[]>
    {
        public AddFilesRequest(string apiKey,
                               string token,
                               string boardId,
                               List<(string name, int size,string fullPath)> files) :
            base(string.Format(RequestUrisV2.AddFiles, boardId),
                 HttpMethod.Post,
                 apiKey,
                 token,
                 (new AddFilesRequestContent(files)).Files)
        { }
    }

    internal class BoardFilePartUploadUrlRequestV2 : TokenizedWeTransferHttpRequestMessage<object>
    {
        public BoardFilePartUploadUrlRequestV2(string apiKey,
                                               string token,
                                               string boardId,
                                               string fileId,
                                               string multiPartFileId,
                                               int partNumber) :
            base(string.Format(RequestUrisV2.BoardFileUploadUrl, boardId, fileId, partNumber,multiPartFileId),
                               HttpMethod.Get,
                               apiKey,
                               token,
                               content: null)
        { }
    }

    internal class BoardFileUploadCompleteRequestV2 : TokenizedWeTransferHttpRequestMessage<object>
    {
        public BoardFileUploadCompleteRequestV2(string apiKey,
                                                 string token,
                                                 string boardId,
                                                 string fileId) : 
            base(string.Format(RequestUrisV2.BoardFileUploadComplete, boardId, fileId),
                 HttpMethod.Put,
                 apiKey,
                 token,
                 content: null)
        {
            //In order to get the Content-Type header added to the request some dummy content must be created.
            AddDummyData();
        }

    }
    #endregion
}
