# Dino Garage thumbnails

These transparent thumbnails are deterministic renders of the species GLB models
served by IslePilot under `https://islepilot.eu/cdn/skinviewer/` on 2026-09-04.
The camera, model scale, and model position mirror IslePilot's Garage viewer.

The packaged thumbnails contain neutral lighting only. At runtime the Garage card
applies the stored dinosaur's body color and displays its complete palette below
the preview. This avoids downloading a third-party species image and keeps the
preview available when IslePilot's image CDN is temporarily slow.

Regenerate with `scripts/render-dino-thumbnails.mjs` and
`scripts/dino-thumbnail-renderer.html`. IslePilot currently exposes no 3D model
for Baryonyx, so that species intentionally uses the native fallback card.
