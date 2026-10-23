# GlassDock.Windows

Windows Shell, HWND, Win32, and COM adapters. Platform details stay behind narrow interfaces.

The first M2 Shell slice defines the provider contract and a pure adapter from provider-resolved
AUMID/Shell Link identities to Core pinned items. It performs no application discovery, icon
extraction, process launch, or window activation yet. AUMID comparisons follow Core's scheme-aware
identity rules; Shell Link alias resolution must supply a canonical key from a future Shell resolver.
The icon contract returns UI-framework-neutral PNG bytes, but retrieving local icons is not yet
implemented and does not grant redistribution rights for third-party artwork.
