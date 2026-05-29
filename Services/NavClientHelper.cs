using System.ServiceModel;
using ParcelAPI.Models;

namespace ParcelAPI.Services
{
    public static class NavClientHelper
    {
        public static dynamic InitializeClient<T>(Client cl)
        {
            string Namespace = typeof(T).Namespace!;
            string Class_Name = typeof(T).Name;

            var clientType = Type.GetType($"{Namespace}.{Class_Name}_PortClient");
            if (clientType == null)
            {
                throw new InvalidOperationException($"Client type {Namespace}.{Class_Name}_PortClient not found");
            }

            var address = new EndpointAddress(BaseUrl(cl) + Class_Name);
            dynamic client = Activator.CreateInstance(clientType, Binding(), address)!;
            client.ClientCredentials.Windows.AllowedImpersonationLevel = System.Security.Principal.TokenImpersonationLevel.Delegation;
            client.ClientCredentials.Windows.ClientCredential.UserName = cl.UserName;
            client.ClientCredentials.Windows.ClientCredential.Password = cl.Password;
            client.ClientCredentials.UserName.UserName = cl.UserName;
            client.ClientCredentials.UserName.Password = cl.Password;
            return client;
        }

        public static dynamic InitializeClientCodeunit<T>(Client cl)
        {
            string Namespace = typeof(T).Namespace!;
            string Class_Name = typeof(T).Name;

            var clientType = Type.GetType($"{Namespace}.{Class_Name}");
            if (clientType == null)
            {
                throw new InvalidOperationException($"Client type {Namespace}.{Class_Name} not found");
            }

            var address = new EndpointAddress(BaseUrlCodeunit(cl) + Class_Name);
            dynamic client = Activator.CreateInstance(clientType, Binding(), address)!;
            client.ClientCredentials.Windows.AllowedImpersonationLevel = System.Security.Principal.TokenImpersonationLevel.Delegation;
            client.ClientCredentials.Windows.ClientCredential.UserName = cl.UserName;
            client.ClientCredentials.Windows.ClientCredential.Password = cl.Password;
            client.ClientCredentials.UserName.UserName = cl.UserName;
            client.ClientCredentials.UserName.Password = cl.Password;
            return client;
        }

        private static BasicHttpBinding Binding()
        {
            var navWSBinding = new BasicHttpBinding
            {
                SendTimeout = TimeSpan.FromMinutes(5),
                MaxBufferSize = int.MaxValue,
                MaxReceivedMessageSize = int.MaxValue,
                Security = new BasicHttpSecurity
                {
                    Mode = BasicHttpSecurityMode.TransportCredentialOnly,
                    Transport = new HttpTransportSecurity
                    {
                        ClientCredentialType = HttpClientCredentialType.Windows
                    }
                }
            };
            navWSBinding.ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max;
            return navWSBinding;
        }

        private static string BaseUrl(Client cl)
        {
            var host = cl.IPAddress ?? "localhost";
            var port = cl.Port ?? 7047;
            var instance = cl.Instance ?? "NAV";
            var company = cl.Company ?? "Company";

            return $"http://{host}:{port}/{instance}/WS/{Uri.EscapeDataString(company)}/Page/";
        }

        private static string BaseUrlCodeunit(Client cl)
        {
            var host = cl.IPAddress ?? "localhost";
            var port = cl.Port ?? 7047;
            var instance = cl.Instance ?? "NAV";
            var company = cl.Company ?? "Company";

            return $"http://{host}:{port}/{instance}/WS/{Uri.EscapeDataString(company)}/Codeunit/";
        }
    }
}
