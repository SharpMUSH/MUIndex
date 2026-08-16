// The WebAuthn ceremony, and the only JavaScript this site has.
//
// Everything else here — the listing, the game pages, the archive, plain mode, the API, the facet
// panel — works with scripting disabled, and that is a design constraint rather than an accident.
// navigator.credentials has no scripting-off path, so the boundary is drawn at sign-in: the part
// that needs a script is the part used by people who administer a game server.
//
// No framework, no bundler, no external host. A page that asks somebody to prove they control a
// server should not also be asking them to trust a CDN.

(() => {
  const post = async (url, body) => {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error((await response.text()) || response.statusText);
    }

    return response.status === 204 ? null : await response.json();
  };

  // Some password managers implement PublicKeyCredential.toJSON incorrectly, and JSON.stringify then
  // throws "Illegal invocation" at the moment of registering — which reads to the operator as the
  // site being broken. Microsoft documents this workaround; it serialises the credential by hand
  // rather than relying on the browser's own toJSON.
  const base64url = (value) => {
    if (!value) {
      return undefined;
    }

    let bytes = value;
    if (Array.isArray(bytes)) bytes = Uint8Array.from(bytes);
    if (bytes instanceof ArrayBuffer) bytes = new Uint8Array(bytes);

    if (bytes instanceof Uint8Array) {
      let binary = '';
      for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
      bytes = window.btoa(binary);
    }

    if (typeof bytes !== 'string') {
      throw new Error('Could not serialise the credential.');
    }

    return bytes.replace(/\+/g, '-').replace(/\//g, '_').replace(/=*$/g, '');
  };

  const serialise = (credential) => JSON.stringify({
    authenticatorAttachment: credential.authenticatorAttachment,
    clientExtensionResults: credential.getClientExtensionResults(),
    id: credential.id,
    rawId: base64url(credential.rawId),
    response: {
      attestationObject: base64url(credential.response.attestationObject),
      authenticatorData: base64url(
        credential.response.authenticatorData ??
        credential.response.getAuthenticatorData?.() ??
        undefined),
      clientDataJSON: base64url(credential.response.clientDataJSON),
      publicKey: base64url(credential.response.getPublicKey?.() ?? undefined),
      publicKeyAlgorithm: credential.response.getPublicKeyAlgorithm?.() ?? undefined,
      transports: credential.response.getTransports?.() ?? undefined,
      signature: base64url(credential.response.signature),
      userHandle: base64url(credential.response.userHandle),
    },
    type: credential.type,
  });

  const say = (form, message) => {
    const status = form.querySelector('[data-passkey-status]');
    if (status) {
      status.textContent = message;
    }
  };

  const run = async (form, action) => {
    const name = form.querySelector('[name="name"]')?.value ?? '';
    const registering = action === 'register';

    const optionsJson = registering
      ? await post(`/account/passkey/registration-options?name=${encodeURIComponent(name)}`)
      : await post('/account/passkey/assertion-options');

    const credential = registering
      ? await navigator.credentials.create({
          publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(optionsJson),
        })
      : await navigator.credentials.get({
          publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson),
        });

    // The credential alone. The name went with the request that built the options, and the server
    // keeps it there — posting it again would be offering a second answer to a settled question.
    const result = await post(
      registering ? '/account/passkey/register' : '/account/passkey/sign-in',
      { credential: serialise(credential) });

    window.location.assign(result.redirect);
  };

  document.querySelectorAll('form[data-passkey]').forEach((form) => {
    const action = form.dataset.passkey;

    // The button is disabled in the markup and enabled here, so a browser with scripting off shows
    // the explanation beside it rather than a control that does nothing when pressed.
    const button = form.querySelector('button');
    if (button) {
      button.disabled = false;
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      say(form, 'Waiting for your device…');

      try {
        await run(form, action);
      } catch (error) {
        // NotAllowedError is the browser's word for "the person cancelled, or it timed out", and it
        // is not a failure worth alarming anybody about.
        say(form, error?.name === 'NotAllowedError'
          ? 'No passkey was used. Try again when you are ready.'
          : `That did not work: ${error?.message ?? error}`);
      }
    });
  });

  // WebAuthn needs a secure context. Saying so is better than a button that fails on click.
  if (!window.PublicKeyCredential) {
    document.querySelectorAll('[data-passkey-status]').forEach((status) => {
      status.textContent =
        'This browser has no passkey support, or the page is not being served over HTTPS.';
    });
  }
})();
