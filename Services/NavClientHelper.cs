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

            // Derive the NAV codeunit name from the WCF ClientBase<T> hierarchy
            // instead of using the _PortClient class name directly.
            // T (e.g. MBranch_PortClient) inherits from ClientBase<TInterface>
            // where TInterface (e.g. MBranch_Port) has the ServiceContract attribute.
            // The NAV service name is the interface name minus the "_Port" suffix.
            string serviceName = DeriveServiceName(typeof(T));

            var address = new EndpointAddress(BaseUrlCodeunit(cl) + serviceName);
            dynamic client = Activator.CreateInstance(clientType, Binding(), address)!;
            client.ClientCredentials.Windows.AllowedImpersonationLevel = System.Security.Principal.TokenImpersonationLevel.Delegation;
            client.ClientCredentials.Windows.ClientCredential.UserName = cl.UserName;
            client.ClientCredentials.Windows.ClientCredential.Password = cl.Password;
            client.ClientCredentials.UserName.UserName = cl.UserName;
            client.ClientCredentials.UserName.Password = cl.Password;
            return client;
        }

        /// <summary>
        /// Derives the NAV service name from a WCF ClientBase&lt;T&gt; proxy type.
        /// For a type like <c>MBranch_PortClient</c> (which extends <c>ClientBase&lt;MBranch_Port&gt;</c>),
        /// extracts the interface name <c>MBranch_Port</c> and strips the <c>_Port</c> suffix
        /// to produce the actual NAV codeunit name <c>MBranch</c>.
        /// Falls back to stripping <c>_PortClient</c> from the class name if the type
        /// hierarchy does not match the expected pattern.
        /// </summary>
        private static string DeriveServiceName(Type clientType)
        {
            const string portClientSuffix = "_PortClient";
            const string portSuffix = "_Port";

            // Check if the type inherits from ClientBase<>, which is the WCF proxy pattern
            var baseType = clientType.BaseType;
            if (baseType != null && baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition() == typeof(ClientBase<>))
            {
                // Get the service contract interface (e.g., MBranch_Port)
                var interfaceType = baseType.GetGenericArguments()[0];
                string interfaceName = interfaceType.Name;

                // Strip the _Port suffix to get the NAV service name
                if (interfaceName.EndsWith(portSuffix))
                {
                    return interfaceName.Substring(0, interfaceName.Length - portSuffix.Length);
                }

                // If no _Port suffix, use the interface name as-is
                return interfaceName;
            }

            // Fallback: strip _PortClient from the class name
            if (clientType.Name.EndsWith(portClientSuffix))
            {
                return clientType.Name.Substring(0, clientType.Name.Length - portClientSuffix.Length);
            }

            return clientType.Name;
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
