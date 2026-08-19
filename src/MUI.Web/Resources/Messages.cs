namespace MUI.Web.Resources;

/// <summary>
/// The marker <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> resolves against.
/// </summary>
/// <remarks>
/// A type with no members, whose only job is to name a resource set: <c>Messages.resx</c> and every
/// <c>Messages.&lt;culture&gt;.resx</c> beside it are discovered from this class's namespace and
/// compiled into satellite assemblies by the SDK. Values are ICU patterns, not composite-format
/// strings — <c>string.Format</c>'s <c>{0}</c> can only substitute, so "23 games" would have to be
/// assembled from a number and a noun in C#; <see cref="Localization.IcuMessage"/> supplies the
/// grammar resx alone cannot.
/// </remarks>
public sealed class Messages;
