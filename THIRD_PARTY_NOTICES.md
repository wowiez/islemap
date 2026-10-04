# Third-party map data

`src/TheIsleOverlay.App/Assets/SbtcWildlifeIcons.json` contains wildlife SVG path and colour data extracted from the public [SBTC Island map icon catalogue](https://sbtcislandd.com/js/mapicons.js) on 2026-10-01. The artwork is used to identify the live AI creatures returned by the server's public `/api/ai_positions` feed. This project does not claim ownership of the artwork.

`src/TheIsleOverlay.App/Assets/GatewayDrinkingWater.webp` is the Gateway drinking-water raster overlay sourced from [VulnonaMAP](https://vulnona.com/game/map/), specifically the public Gateway map layer at `https://vulnona.com/game/map/map/Gateway_v0.21.7/water/4.webp`.

VulnonaMAP describes its non-basemap data as collective intelligence and asks users to use their own judgment when handling it. The map is an unofficial fan-made service; the underlying planemap remains subject to the game developers' rights. This project does not claim ownership of the overlay.

# Meshopt decoder

`src/TheIsleOverlay.App/Assets/GarageViewer/vendor/meshopt_decoder.module.js` is the original Meshopt decoder bundled with Three.js r180, built from meshoptimizer 0.22.
Source: https://github.com/mrdoob/three.js/blob/r180/examples/jsm/libs/meshopt_decoder.module.js

Copyright (c) 2016-2024 Arseny Kapoulkine

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
