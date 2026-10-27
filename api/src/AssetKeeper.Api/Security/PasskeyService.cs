using AssetKeeper.Infrastructure.Persistence;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Api.Security;

public sealed class PasskeyService(
    IFido2 fido2,
    AppDbContext database,
    PasskeyOptions options,
    ILogger<PasskeyService> logger)
{
    public bool Enabled => options.Enabled;

    public Task<bool> HasCredentialsAsync(CancellationToken cancellationToken)
    {
        return database.PasskeyCredentials.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PasskeyCredential>> ListAsync(CancellationToken cancellationToken)
    {
        return await database.PasskeyCredentials
            .AsNoTracking()
            .OrderByDescending(credential => credential.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(CancellationToken cancellationToken)
    {
        var existing = await database.PasskeyCredentials
            .Select(credential => credential.CredentialId)
            .ToListAsync(cancellationToken);

        return fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = PasskeyOwner.UserHandle,
                Name = "owner",
                DisplayName = PasskeyOwner.DisplayName
            },
            ExcludeCredentials = existing
                .Select(id => new PublicKeyCredentialDescriptor(PublicKeyCredentialType.PublicKey, id))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Preferred
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
    }

    public async Task<PasskeyCredential> CompleteRegistrationAsync(
        AuthenticatorAttestationRawResponse response,
        CredentialCreateOptions originalOptions,
        string? label,
        CancellationToken cancellationToken)
    {
        var registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = originalOptions,
            IsCredentialIdUniqueToUserCallback = async (args, token) =>
                !await database.PasskeyCredentials.AnyAsync(
                    credential => credential.CredentialId == args.CredentialId,
                    token)
        }, cancellationToken);

        var credential = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            CredentialId = registered.Id,
            PublicKey = registered.PublicKey,
            SignCount = registered.SignCount,
            Label = NormalizeLabel(label),
            Transports = registered.Transports is { Length: > 0 }
                ? string.Join(',', registered.Transports.Select(transport => transport.ToString().ToLowerInvariant()))
                : null,
            BackupEligible = registered.IsBackupEligible,
            AaGuid = registered.AaGuid,
            CreatedAt = DateTimeOffset.UtcNow
        };

        database.PasskeyCredentials.Add(credential);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Passkey 凭据保存失败，可能是重复注册。");
            throw new InvalidOperationException("该 Passkey 已经注册过，请直接使用它登录。", exception);
        }

        return credential;
    }

    public async Task<AssertionOptions> CreateAssertionOptionsAsync(CancellationToken cancellationToken)
    {
        var descriptors = await database.PasskeyCredentials
            .AsNoTracking()
            .Select(credential => new PublicKeyCredentialDescriptor(
                PublicKeyCredentialType.PublicKey,
                credential.CredentialId))
            .ToListAsync(cancellationToken);

        return fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            // 列出已注册凭据；挑战本身不含用户名，登录时由凭据直接识别本人。
            AllowedCredentials = descriptors,
            UserVerification = UserVerificationRequirement.Preferred
        });
    }

    public async Task<PasskeyCredential> CompleteAssertionAsync(
        AuthenticatorAssertionRawResponse response,
        AssertionOptions originalOptions,
        CancellationToken cancellationToken)
    {
        var credential = await database.PasskeyCredentials.FirstOrDefaultAsync(
            stored => stored.CredentialId == response.RawId,
            cancellationToken);

        if (credential is null)
        {
            throw new InvalidOperationException("未找到对应的 Passkey，请重新注册或使用访问口令登录。");
        }

        var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = response,
            OriginalOptions = originalOptions,
            StoredPublicKey = credential.PublicKey,
            StoredSignatureCounter = credential.SignCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                Task.FromResult(args.UserHandle.SequenceEqual(PasskeyOwner.UserHandle))
        }, cancellationToken);

        credential.SignCount = result.SignCount;
        credential.LastUsedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        return credential;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var credential = await database.PasskeyCredentials.FirstOrDefaultAsync(
            stored => stored.Id == id,
            cancellationToken);

        if (credential is null)
        {
            return false;
        }

        database.PasskeyCredentials.Remove(credential);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "此设备";
        }

        return trimmed.Length > 60 ? trimmed[..60] : trimmed;
    }
}
