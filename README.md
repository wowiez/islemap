# Isle Live Map

Ứng dụng overlay cho **The Isle Evrima**. Isle Live Map chạy ngoài process game, luôn nổi trên màn hình và hiển thị minimap Gateway cùng telemetry cá nhân từ nguồn server tương thích.

## Nguồn telemetry

- **IslePilot Network** — đăng nhập Steam một lần và tự nhận server hiện tại; hỗ trợ DinoVietNam, Premium, HoHo cùng các server đã cài plugin IslePilot.
- **EraGaming** — kết nối trực tiếp bằng phiên đăng nhập tại `https://eragamingvn.net/live-map`.
- **PANDORA** — kết nối trực tiếp bằng phiên đăng nhập tại `https://islapandora.eu/live-map`.

Texture Gateway dùng bản EraGaming/MyIsleMap đóng gói trong app. Đây chỉ là ảnh nền local; telemetry và marker đến từ nguồn đã chọn.

## Tính năng

- Minimap Gateway dạng tròn dùng texture EraGaming/MyIsleMap đóng gói local, không tải ảnh map qua mạng khi khởi động.
- Marker luôn giữ giữa viewport; bản đồ xoay theo hướng nhìn để phía trước của khủng long luôn hướng lên trên.
- Tọa độ, yaw và status realtime từ WebSocket `/ows`; `/api/overlay/me` và `/api/overlay/map` làm baseline/fallback.
- Growth, Health, Stamina, Hunger và Water khi nguồn cung cấp trường tương ứng.
- Home có Steam Login cho IslePilot và hai nút riêng cho EraGaming/PANDORA, tránh dùng nhầm phiên giữa các website.
- Token overlay được mã hóa bằng Windows DPAPI cho tài khoản Windows hiện tại; không lưu plaintext.
- Tab `GARAGE` trong F8 đọc danh sách Dino đã lưu cùng thumbnail dựng từ model IslePilot, Growth/Health/Food/Water/Stamina và palette từ `/api/overlay/garage`; có thể cất Dino hiện tại hoặc lấy/đổi Dino đã lưu sau bước xác nhận.
- Tự reconnect với backoff, giữ snapshot cuối và báo `RECONNECTING`/`DATA STALE` khi mạng yếu.
- HUD dọc, nền ngoài trong suốt, always-on-top và click-through khi đóng Settings.
- Một bảng Settings tổng cho phép resize HUD 65–175%, kéo vị trí, kéo resize, chỉnh crop/zoom, chọn minimap tròn/vuông và bật tắt riêng Map hoặc Activity; thiết lập được tự lưu.
- Bảng nhiệm vụ Prime nhận tự động từ telemetry, mặc định tắt và có thể bật trong Settings; mỗi nhiệm vụ chỉ hiển thị trạng thái hoàn thành mà server cung cấp.
- Khi chơi đúng server SBTC, minimap tự vẽ Sanctuary, Migration/MMZ và Patrol Zone; dữ liệu `/api/overlay/map` được ưu tiên và bộ Gateway zone đóng gói offline được dùng khi server không trả polygon.
- Bản cài đặt tự kiểm tra GitHub Release một lần khi mở Home, hiện dialog khi có bản mới và chỉ tải/cập nhật sau khi người dùng xác nhận; portable không tự cập nhật.

## Phím tắt

| Phím | Tác dụng |
|---|---|
| `Ctrl + Shift + O` | Mở / đóng bảng Settings tổng |

Phím tắt hoạt động kể cả khi game hoặc ứng dụng khác đang focus. Ứng dụng không còn cài low-level mouse hook.

Bấm `Ctrl + Shift + O`, sau đó dùng thanh `HUD SIZE`, `MAP CROP`, lựa chọn `CIRCLE / SQUARE`, công tắc `MAP / ACTIVITY`, vùng `MOVE HUD` hoặc tay nắm `SIZE ↘`. Bấm `DONE` hoặc dùng lại phím tắt để đóng Settings và trả overlay về chế độ xuyên chuột.

## Đăng nhập và quyền riêng tư

App mở website đăng nhập trong Microsoft Edge WebView2 với profile riêng tại:

```text
%LocalAppData%\IsleLiveMapData\WebView2
```

Callback IslePilot `isle-overlay://` được bắt ngay bên trong WebView2. Isle Live Map không đăng ký hoặc chiếm protocol này trong Windows.

Overlay Bearer token được lưu tại `%LocalAppData%\IsleLiveMapData` sau khi mã hóa bằng DPAPI CurrentUser. Thư mục này tách biệt với install root `%LocalAppData%\IsleLiveMap` do Velopack quản lý. Token:

- Không được ghi vào log, source, `.env` hoặc JSON plaintext.
- Chỉ được gửi tới host cố định `https://islepilot.eu` và `wss://islepilot.eu/ows`.
- Chỉ dùng để đọc `/api/overlay/me`, `/api/overlay/map` và frame `live` từ `/ows`.
- Bị xóa khi người dùng đăng xuất hoặc API trả 401/403.

Với nguồn trực tiếp, app chỉ đọc cookie từ đúng host đang mở: `era_session` chỉ được gửi lại `eragamingvn.net`; phiên PANDORA chỉ được gửi lại `islapandora.eu`. WebView2 quản lý cookie trong profile riêng; header dùng gọi API chỉ được ghép trong bộ nhớ và không được ghi vào log hoặc source.

## Yêu cầu chạy

- Windows 10/11 x64.
- Microsoft Edge WebView2 Runtime (đã có sẵn trên hầu hết Windows 10/11 hiện tại).
- Game ở Borderless hoặc Windowed; Exclusive Fullscreen có thể che overlay WPF.

Dùng installer do nhà phát hành cung cấp nếu muốn nhận auto-update. Portable luôn cập nhật thủ công.

## Build từ source

Yêu cầu .NET 8 SDK:

```powershell
dotnet tool restore
dotnet restore .\TheIsleOverlay.sln
dotnet test .\TheIsleOverlay.sln --configuration Release
dotnet build .\TheIsleOverlay.sln --configuration Release
```

Build installer:

```powershell
.\scripts\Package-Release.ps1 -Version 1.7.13
```

Output nằm trong `artifacts/distribution`.

## Phát hành và auto-update

Từ bản 1.7.6, app cài bằng Setup dùng Velopack để kiểm tra GitHub Release. Bản portable/dev bị bỏ qua và lỗi update không ảnh hưởng luồng sử dụng chính.

Luồng auto-update hiện tại:

1. GitHub Actions chạy khi push tag `vX.Y.Z`, test rồi gọi `scripts/Package-Release.ps1`.
2. Toàn bộ file trong `artifacts/distribution` được đính kèm vào GitHub Release, đặc biệt là `releases.win.json`, full `.nupkg` và installer.
3. Bản cài đặt dùng `Velopack.UpdateManager` với `SimpleWebSource` trỏ thẳng tới `https://github.com/wowiez/islemap/releases/latest/download/`; không gọi GitHub REST API và không nhúng GitHub token vào app.
4. App chỉ kiểm tra một lần sau khi Home đã mở. Khi có bản mới, dialog cho phép cập nhật ngay hoặc bỏ qua; nếu bỏ qua, footer giữ trạng thái `CẦN UPDATE` và nút `UPDATE`.
5. Portable/dev build không tự cập nhật; luồng update chỉ bật khi bản hiện tại là package đã cài (`CurrentVersion` có giá trị và `IsPortable` là false).

Mỗi release phải đồng bộ version ở project, release notes và tag Git. Ví dụ:

```powershell
git tag v1.7.6
git push origin main
git push origin v1.7.6
```

Không tạo lại cùng một tag/version. Nếu workflow release lỗi, sửa nguyên nhân rồi chạy lại workflow hoặc phát hành version mới.

## Kiến trúc

```text
TheIsleOverlay.App        Home, Steam WebView2 login, WPF overlay và global shortcut
TheIsleOverlay.Core       Telemetry session contract, reducer support, projection và heading
TheIsleOverlay.EraGaming  Adapter JSON API EraGaming
TheIsleOverlay.IslePilot  Bearer REST, WebSocket realtime, auth, reducer và DPAPI credential store
TheIsleOverlay.Pandora    Adapter session API PANDORA
TheIsleOverlay.Tests      Unit/integration tests auth, transport, reducer, projection và heading
```

`ITelemetrySession` là ranh giới của UI. `MainWindow` chỉ nhận `TelemetrySnapshot`; vòng REST, WebSocket, reconnect và stale detection nằm ngoài cửa sổ.

Texture nền Gateway được nhúng vào ứng dụng. Các provider chỉ lấy telemetry như tọa độ, yaw và status để overlay vẽ lên texture local; chúng không cung cấp hoặc tải ảnh map.

## Giới hạn

- IslePilot dùng WebSocket realtime; EraGaming và PANDORA cập nhật theo nhịp API do website tương ứng cho phép.
- Mất Internet không ảnh hưởng texture map local nhưng telemetry sẽ chuyển `RECONNECTING` hoặc `DATA STALE`.
- Website nguồn thay đổi endpoint, callback hoặc payload có thể yêu cầu cập nhật client.
- Texture Gateway local được cập nhật theo từng bản phát hành của ứng dụng khi map game thay đổi.
- Bản phát hành chưa được ký bằng chứng thư thương mại, vì vậy Windows SmartScreen có thể cảnh báo ở lần chạy đầu.

## License

[MIT](LICENSE) — sử dụng, kiểm tra và đóng góp tự do; vui lòng giữ thông báo bản quyền.
