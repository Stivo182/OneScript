/*----------------------------------------------------------
This Source Code Form is subject to the terms of the 
Mozilla Public License, v.2.0. If a copy of the MPL 
was not distributed with this file, You can obtain one 
at http://mozilla.org/MPL/2.0/.
----------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using OneScript.Contexts;
using OneScript.Exceptions;
using OneScript.StandardLibrary.Collections;
using OneScript.Types;
using RegExp = System.Text.RegularExpressions;
using ScriptEngine.Machine;
using ScriptEngine.Machine.Contexts;

namespace OneScript.StandardLibrary.Http
{
    /// <summary>
    /// Объект доступа к протоколу HTTP/HTTPS.
    /// Использует семантику методов, реализованных в платформе 1С:Предприятие 8.2.18 и старше.
    /// Синтаксис методов, применявшийся в более младших версиях не поддерживается.
    /// Средства работы с HTTP находятся в статусе experimental.
    /// </summary>
    [ContextClass("HTTPСоединение", "HTTPConnection")]
    public class HttpConnectionContext : AutoContext<HttpConnectionContext>
    {
        readonly InternetProxyContext _proxy;

        readonly Uri _hostUri;

        const string HTTP_SCHEME = "http";
        const string HTTPS_SCHEME = "https";

        public HttpConnectionContext(string host,
            int port = 0,
            string user = null,
            string password = null,
            InternetProxyContext proxy = null,
            int timeout = 0,
            IValue ssl = null,
            bool useOSAuth = false)
        {
            if (ssl != null && !(ssl.SystemType == BasicTypes.Undefined || ssl.IsSkippedArgument()))
                throw new RuntimeException("Защищенное соединение по произвольным сертификатам не поддерживается. Если необходим доступ по https, просто укажите протокол https в адресе хоста.");
            
            var uriBuilder = new UriBuilder(host);
            if (port != 0)
                uriBuilder.Port = port;

            if (uriBuilder.Scheme != HTTP_SCHEME && uriBuilder.Scheme != HTTPS_SCHEME)
                throw RuntimeException.InvalidArgumentValue();

            _hostUri = uriBuilder.Uri;

            Host = _hostUri.Host;
            Port = _hostUri.Port;

            User = user == null ? String.Empty : user;
            Password = password == null ? String.Empty : password;

            Timeout = timeout;
            _proxy = proxy;
            UseOSAuthentication = useOSAuth;
            AllowAutoRedirect = true;
        }

        [ContextProperty("ИспользоватьАутентификациюОС", "UseOSAuthentication", CanWrite=false)]
        public bool UseOSAuthentication
        {
            get;
            set;
        }

        [ContextProperty("Пользователь","User")]
        public string User 
        { 
            get; private set;
        }

        [ContextProperty("Пароль", "Password")]
        public string Password
        {
            get; private set;
            
        }

        [ContextProperty("Сервер", "Host")]
        public string Host
        {
            get; private set;
        }

        [ContextProperty("Порт", "Port")]
        public int Port
        {
            get; private set;
        }

        [ContextProperty("Прокси", "Proxy")]
        public IValue Proxy
        {
            get
            {
                if (_proxy == null)
                    return ValueFactory.Create();

                return _proxy;
            }
        }

        [ContextProperty("Таймаут", "Timeout")]
        public int Timeout
        {
            get; private set;
        }

        [ContextProperty("РазрешитьАвтоматическоеПеренаправление", "AllowAutoRedirect")]
        public bool AllowAutoRedirect { get; set; }

        /// <summary>
        /// Получить данные методом GET
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <param name="output">Строка. Имя файла, в который нужно записать ответ. Необязательный параметр.</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("Получить", "Get")]
        public HttpResponseContext Get(HttpRequestContext request, string output = null)
        {
            return GetResponse(request, "GET", output);
        }

        /// <summary>
        /// Передача данных методом PUT
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("Записать", "Put")]
        public HttpResponseContext Put(HttpRequestContext request)
        {
            return GetResponse(request, "PUT");
        }

        /// <summary>
        /// Передача данных методом POST
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <param name="output">Строка. Имя файла, в который нужно записать ответ. Необязательный параметр.</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("ОтправитьДляОбработки", "Post")]
        public HttpResponseContext Post(HttpRequestContext request, string output = null)
        {
            return GetResponse(request, "POST", output);
        }

        /// <summary>
        /// Удалить данные методом DELETE
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("Удалить", "Delete")]
        public HttpResponseContext Delete(HttpRequestContext request)
        {
            return GetResponse(request, "DELETE");
        }

        /// <summary>
        /// Изменяет данные на сервере при помощи PATCH-запроса
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("Изменить", "Patch")]
        public HttpResponseContext Patch(HttpRequestContext request)
        {
            return GetResponse(request, "PATCH");
        }

        /// <summary>
        /// Получает при помощи HEAD-запроса информацию о запрашиваемых данных, содержащуюся в заголовках, не получая сами данные.
        /// </summary>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("ПолучитьЗаголовки", "Head")]
        public HttpResponseContext Head(HttpRequestContext request)
        {
            return GetResponse(request, "HEAD");
        }

        /// <summary>
        /// Вызвать произвольный HTTP-метод
        /// </summary>
        /// <param name="method">Строка. Имя метода HTTP</param>
        /// <param name="request">HTTPЗапрос. Данные и заголовки запроса http</param>
        /// <param name="output">Строка. Имя выходного файла</param>
        /// <returns>HTTPОтвет. Ответ сервера.</returns>
        [ContextMethod("ВызватьHTTPМетод", "CallHTTPMethod")]
        public HttpResponseContext Patch(string method, HttpRequestContext request, string output = null)
        {
            return GetResponse(request, method, output);
        }
        
        private static bool ContentBodyAllowed(string method)
        {
            var methods = new List<string> {"GET", "CONNECT", "HEAD"};
            return !methods.Contains(method, StringComparer.OrdinalIgnoreCase);
        }
        
        private static void SetRequestBody(HttpRequestContext request, HttpRequestMessage requestMessage)
        {
            var stream = request.Body;
            if (stream != null)
            {
                requestMessage.Content = new StreamContent(stream);
            }
        }

        private static void SetRequestHeaders(HttpRequestContext request, HttpRequestMessage requestMessage)
        {
            foreach (var item in request.Headers.Select(x => x.GetRawValue() as KeyAndValueImpl))
            {
                System.Diagnostics.Trace.Assert(item != null);

                var key = item.Key.ToString();
                var value = item.Value.ToString();

                switch (key.ToUpperInvariant())
                {
                    case "CONTENT-TYPE":
                        if( requestMessage.Content != null)
                            requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(value);
                        break;
                    case "CONTENT-LENGTH":
                        try
                        {
                            if( requestMessage.Content != null)
                                requestMessage.Content.Headers.ContentLength = int.Parse(value);
                        }
                        catch (FormatException)
                        {
                            throw new RuntimeException("Заголовок Content-Length задан неправильно");
                        }
                        break;
                    case "ACCEPT":
                        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(value));
                        break;
                    case "EXPECT":
                        requestMessage.Headers.Expect.Add(new NameValueWithParametersHeaderValue(value));
                        break;
                    case "TRANSFER-ENCODING":
                        requestMessage.Headers.TransferEncoding.Add(new TransferCodingHeaderValue(value));
                        break;
                    case "CONNECTION":
                        if (value.Equals("KEEP-ALIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            requestMessage.Headers.ConnectionClose = false;
                        }
                        else if (value.Equals("CLOSE", StringComparison.OrdinalIgnoreCase))
                        {
                            requestMessage.Headers.ConnectionClose = true;
                        }
                        else
                        {
                            requestMessage.Headers.Connection.Add(value);
                        }
                        break;
                    case "DATE":
                        try 
	                    {	        
                            requestMessage.Headers.Date = DateTime.Parse(value);
	                    }
	                    catch (FormatException)
	                    {
		                    throw new RuntimeException("Заголовок Date задан неправильно");
	                    }
                        break;
                    case "HOST":
                        requestMessage.Headers.Host = value;
                        break;
                    case "IF-MODIFIED-SINCE":
                        try
                        {
                            requestMessage.Headers.IfModifiedSince = DateTime.Parse(value);
                        }
                        catch (FormatException)
                        {
                            throw new RuntimeException("Заголовок If-Modified-Since задан неправильно");
                        }
                        break;
                    case "RANGE":
                        try
                        {
                            var rangeHeaderValue = ParseRange(value);
                            if (rangeHeaderValue.Ranges.Count > 0)
                                requestMessage.Headers.Range = rangeHeaderValue;
                        }
                        catch
                        {
                            throw new RuntimeException("Заголовок Range задан неправильно");
                        }
                        break;
                    case "REFERER":
                        requestMessage.Headers.Referrer = new Uri(value);
                        break;
                    case "USER-AGENT":
                        requestMessage.Headers.UserAgent.Add(new ProductInfoHeaderValue(value));
                        break;
                    case "PROXY-CONNECTION":
                        throw new NotImplementedException();
                    default:
                        requestMessage.Headers.Add(key, value);
                        break;
                           
                }
            }

            // fix #1151
            if (!requestMessage.Headers.UserAgent.Any())
            {
                var agent = new ProductInfoHeaderValue("1Script",
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString());
                requestMessage.Headers.UserAgent.Add(agent);
            }
        }
        
        private static RangeHeaderValue ParseRange(string rangeHeader)
        {
            var range = new RangeHeaderValue();

            if (rangeHeader.Length == 0)
                return range;

            RegExp.MatchCollection matches = RegExp.Regex.Matches(rangeHeader, @"^(.+)=([^;]+)");

            range.Unit = matches[0].Groups[1].Value;

            string stringRange = matches[0].Groups[2].Value;
            string[] ranges = stringRange.Split(',');

            for (int i = 0; i < ranges.Length; i++)
            {
                string rangeSpec = ranges[i].Trim();
                
                Int64? from = null, to = null;
                RegExp.MatchCollection fromMatches = RegExp.Regex.Matches(rangeSpec, @"^(\d+)\-$");
                RegExp.MatchCollection fromToMatches = RegExp.Regex.Matches(rangeSpec, @"^(\d+)\-(\d+)$");
                RegExp.MatchCollection toMatches = RegExp.Regex.Matches(rangeSpec, @"^\-(\d+)$");

                if (fromMatches.Count > 0)
                {
                    from = Int64.Parse(fromMatches[0].Groups[1].Value);
                }
                else if (fromToMatches.Count > 0)
                {
                    from = Int64.Parse(fromToMatches[0].Groups[1].Value);
                    to = Int64.Parse(fromToMatches[0].Groups[2].Value);
                }
                else if (toMatches.Count > 0)
                {
                    to = Int64.Parse(toMatches[0].Groups[1].Value);
                }
                range.Ranges.Add(new RangeItemHeaderValue(from, to));
            }
            return range;
        }
        
        private HttpResponseContext GetResponse(HttpRequestContext request, string method, string output = null)
        {
            HttpRequestMessage requestMessage = CreateRequest(method, request.ResourceAddress);
            
            if (ContentBodyAllowed(method)) 
                SetRequestBody(request, requestMessage);
            
            SetRequestHeaders(request, requestMessage);
            
            using HttpClient client = CreateClient();
            HttpResponseMessage response = client.Send(requestMessage);
            var responseContext = new HttpResponseContext(response, output);

            return responseContext;
        }
        
        private HttpClient CreateClient()
        {
            var uriBuilder = new UriBuilder(_hostUri);
            var handler = new HttpClientHandler();
            
            handler.AllowAutoRedirect = AllowAutoRedirect;

            if (User != "" || Password != "")
            {
                handler.Credentials = new NetworkCredential(User, Password);
            }
            else if (UseOSAuthentication)
            {
                handler.Credentials = CredentialCache.DefaultNetworkCredentials;
            }

            if (_proxy != null)
            {
                handler.Proxy = _proxy.GetProxy(uriBuilder.Scheme);
            }

            if (uriBuilder.Scheme == HTTPS_SCHEME)
            {
                handler.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            }

            var client = new HttpClient(handler);
          
            if (Timeout == 0)
            {
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            }
            else
            {
                client.Timeout = TimeSpan.FromSeconds(Timeout);
            }
            
            return client;
        }
        
        private HttpRequestMessage CreateRequest(string method, string resource)
        {
            var uriBuilder = new UriBuilder(_hostUri);
            if(Port != 0)
                uriBuilder.Port = Port;
            
            var resourceUri = new Uri(uriBuilder.Uri, resource);
            var request = new HttpRequestMessage(new HttpMethod(method), resourceUri);
            
            if (User != "" || Password != "")
            {
                // Авторизация на сервере 1С:Предприятие, например, не работает без явного указания заголовка.
                // http://blog.kowalczyk.info/article/at3/Forcing-basic-http-authentication-for-HttpWebReq.html
                string authInfo = User + ":" + Password;
                // Для 1С работает только UTF-8, хотя стандарт требует ISO-8859-1
                var basicAuthEncoding = Encoding.GetEncoding("UTF-8");
                authInfo = Convert.ToBase64String(basicAuthEncoding.GetBytes(authInfo));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authInfo);
            }
            
            return request;           
        }

        /// <summary>
        /// Стандартный конструктор. Поддержка клиентских сертификатов HTTPS в текущей версии не реализована.
        /// Для доступа к серверу по протоколу HTTPS указывайте схему https:// в URL.
        /// </summary>
        /// <param name="host">Адрес сервера (можно указать URL-схему http или https)</param>
        /// <param name="port">Порт сервера</param>
        /// <param name="user">Пользователь</param>
        /// <param name="password">Пароль</param>
        /// <param name="proxy">ИнтернетПрокси. Настройки прокси-сервера</param>
        /// <param name="timeout">Таймаут ожидания.</param>
        /// <param name="ssl">Объект ЗащищенноеСоединение. На данный момент данная механика работы с SSL не поддерживается. 
        /// Обращение к https возможно, если в адресе хоста указать протокол https. В этом случае будут использованы сертификаты из хранилища ОС.
        /// Указание произвольных клиентских и серверных сертификатов в текущей версии не поддерживается.</param>
        /// <param name="useOSAuthentication">Использовать аутентификацию ОС.</param>
        /// <returns></returns>
        [ScriptConstructor(Name = "По указанному серверу")]
        public static HttpConnectionContext Constructor(
            string host, 
            int port = default, 
            string user = null, 
            string password = null,
            InternetProxyContext proxy = null,
            int timeout = default,
            IValue ssl = null,
            bool useOSAuthentication = default)
        {
            return new HttpConnectionContext(
                host,
                port,
                user,
                password,
                proxy,
                timeout,
                ssl,
                useOSAuthentication);
        }

    }
}
