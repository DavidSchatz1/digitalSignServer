using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace DigitalSignServer.Services;

public sealed class SigningOptions
{
    public string Mode { get; set; } = "UserSecrets";
    public string? PfxBase64 { get; set; }
    public string? PfxPassword { get; set; }

    public SecretsManagerOptions SecretsManager { get; set; } = new();
    public FileOptions File { get; set; } = new();

    public sealed class SecretsManagerOptions
    {
        public string? PfxSecretName { get; set; }
        public string? PasswordSecretName { get; set; }
    }
    public sealed class FileOptions
    {
        public string? Path { get; set; }
        public string? PasswordEnv { get; set; }
    }
}

public interface ISigningCertProvider
{
    X509Certificate2 GetCertificate();
    string Thumbprint { get; }
}

public sealed class SigningCertProvider : ISigningCertProvider
{
    private readonly Lazy<X509Certificate2> _lazy;
    public string Thumbprint => _lazy.Value.Thumbprint ?? "";

    public SigningCertProvider(IOptions<SigningOptions> opt, ILogger<SigningCertProvider> logger)
    {
        _lazy = new Lazy<X509Certificate2>(() =>
        {
            var o = opt.Value;
            byte[] pfx;
            string pass;

            switch (o.Mode)
            {
                case "UserSecrets":
                    pfx = Convert.FromBase64String(
                        o.PfxBase64 ?? throw new InvalidOperationException("Missing Signing:PfxBase64"));
                    pass = o.PfxPassword ?? "";
                    break;

                case "SecretsManager":
                    // TODO: החלף למימוש AWS אמיתי כשנעבור ל-PROD:
                    throw new NotImplementedException("AWS Secrets Manager not wired here yet.");

                case "File":
                    pfx = File.ReadAllBytes(o.File.Path!);
                    pass = Environment.GetEnvironmentVariable(o.File.PasswordEnv!) ?? "";
                    break;

                default:
                    throw new InvalidOperationException("Unknown Signing:Mode");
            }

            var cert = new X509Certificate2(
                pfx,
                pass,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

            logger.LogInformation("Loaded signing cert CN={CN}, Thumbprint={Thumb}",
                cert.GetNameInfo(X509NameType.SimpleName, false), cert.Thumbprint);

            if (!cert.HasPrivateKey)
                throw new InvalidOperationException("Signing certificate must include a private key.");

            return cert;
        });
    }

    public X509Certificate2 GetCertificate() => _lazy.Value;
}

