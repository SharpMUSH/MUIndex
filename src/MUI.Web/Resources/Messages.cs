namespace MUI.Web.Resources;

/// <summary>
/// The marker <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> resolves against.
/// </summary>
/// <remarks>
/// <para>
/// A type with no members, whose only job is to name a resource set: <c>Messages.resx</c> and every
/// <c>Messages.&lt;culture&gt;.resx</c> beside it are discovered from this class's namespace and
/// compiled into satellite assemblies by the SDK, with no <c>&lt;EmbeddedResource&gt;</c> entries in
/// the project file. The same shape SharpMUSH's portal uses for its own chrome.
/// </para>
/// <para>
/// <b>The values are ICU patterns, not composite-format strings.</b> That is where this parts
/// company with the usual .NET arrangement: <c>string.Format</c> and <c>CompositeFormat</c> take
/// <c>{0}</c> and can only substitute, so "23 games" has to be assembled from a number and a noun
/// somewhere in C# — which is the concatenation the whole pipeline exists to remove. resx supplies
/// the storage, the tooling and the satellite assemblies; <see cref="Localization.IcuMessage"/>
/// supplies the grammar.
/// </para>
/// </remarks>
public sealed class Messages;
