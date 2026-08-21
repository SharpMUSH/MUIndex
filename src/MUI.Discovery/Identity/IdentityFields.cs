using MUI.Catalog.Persistence;

namespace MUI.Discovery;

/// <summary>The stored field names the matcher compares on.</summary>
/// <remarks>
/// <see cref="ClaimToken"/> here is a <em>field name</em> and keeps that spelling; the type that reads
/// a beacon off a probe is <see cref="ClaimTokenBeacon"/>.
/// </remarks>
public static class IdentityFields
{
    public const string Name = "name";
    public const string Created = "created";
    /// <summary>The same name the catalogue keeps off the public page; one spelling, one decision.</summary>
    public const string BannerHash = InternalFields.BannerHash;
    public const string Website = "website";
    public const string Contact = "contact";
    public const string Codebase = "codebase";
    public const string ClaimToken = "claim_token";

    /// <summary>The pseudo-field a moved connection address is recorded under in the change feed.</summary>
    public const string Endpoint = "endpoint";
}

/// <summary>The MSSP variables the identity signals are read from.</summary>
public static class IdentityMsspVariables
{
    public const string Name = "NAME";
    public const string Created = "CREATED";
    public const string Website = "WEBSITE";
    public const string Contact = "CONTACT";
    public const string Codebase = "CODEBASE";
}
