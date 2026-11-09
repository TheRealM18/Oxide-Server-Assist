using System;
using System.Security.Cryptography;

namespace QLNPackBootstrap
{
    internal static class PackSignature
    {
        public static bool Verify(byte[] data, byte[] signature, string publicKeyXml, out string error)
        {
            error = null;

            try
            {
                if (!string.IsNullOrEmpty(publicKeyXml) &&
                    (publicKeyXml.IndexOf("<D>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     publicKeyXml.IndexOf("<P>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     publicKeyXml.IndexOf("<Q>", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    error = "PublicKeyXml contains a private key. Use public-key.xml, not private-key.xml.";
                    return false;
                }

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);

                    bool valid;
                    try
                    {
                        valid = rsa.VerifyData(
                            data,
                            signature,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    catch (NotImplementedException)
                    {
                        // Older Mono runtimes use the object-halg overload.
                        valid = rsa.VerifyData(data, "SHA256", signature);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        valid = rsa.VerifyData(data, "SHA256", signature);
                    }

                    if (!valid)
                    {
                        error = "The manifest signature is not valid.";
                    }

                    return valid;
                }
            }
            catch (Exception exception)
            {
                error = "Could not verify the manifest signature: " + exception.Message;
                return false;
            }
        }
    }
}
