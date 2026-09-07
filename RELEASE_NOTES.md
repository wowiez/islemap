# Isle Live Map 1.7.7

- Chuẩn hóa toàn bộ application data, mutex và DPAPI namespace sang `Wowiez`.
- Xóa branding, liên kết và cấu hình runtime cũ khỏi source và tài liệu dự án.
- Do namespace dữ liệu/DPAPI thay đổi, người dùng cần đăng nhập IslePilot và thiết lập HUD lại một lần sau khi cài bản này.

## 1.7.6

- Bật kiểm tra update GitHub một lần sau khi Home mở cho bản cài đặt Velopack; portable/dev không gọi GitHub.
- Tải bản mới ở nền và chỉ áp dụng/khởi động lại sau khi người dùng bấm xác nhận trên Home.
- Thêm workflow phát hành theo tag, kiểm tra tag khớp project version rồi upload đầy đủ feed và package Velopack.
- Lỗi mạng hoặc GitHub không khả dụng không chặn đăng nhập và không làm app thoát.

## 1.7.5

- Lọc riêng màu nước, loại nền tối của drinking-water overlay và làm mềm mép; nước nằm dưới các vùng trên minimap và bản đồ lớn.
- Garage tải model 3D theo nhu cầu, kéo để xoay, cuộn để zoom; cache model trên máy và giải phóng viewer khi đóng tab.
- Bổ sung Windows SDK runtime cần cho WebView2 Composition trong giao diện trong suốt.

## 1.7.3

- Sửa Copy Asset bị mất khi clipboard được copy trước lúc app bật hoặc trong lúc map/F8 đang mở.
- Clipboard chỉ được đánh dấu đã xử lý sau khi app nhận tọa độ thành công; tọa độ hợp lệ bị tạm hoãn sẽ tự thử lại.
- Bổ sung kiểm tra format Asset Location thực tế `-371,863.941, 209,441.418, 24,865.853`.

## 1.7.2

- Sửa vị trí người chơi SBTC bị đứng khi /markers chậm: sau hơn 6 giây không nhận response, dùng self-marker mới từ /map khi có bằng chứng thay đổi tọa độ.
- Giữ nguồn /map cho tới khi /markers có chuyển động mới; response lặp và request bắt đầu trước lần cập nhật nguồn mới không giành lại quyền vị trí.
- Đánh giá độ tươi theo nguồn self-marker được chọn, giữ stats và WS ưu tiên độc lập. Danh sách friend/group vẫn dùng luồng marker riêng.
- Bổ sung regression test outage kéo dài, chuyển nguồn, response chậm/lặp, WS phục hồi và cả hai nguồn hết hạn.

## 1.7.1

- Hiển thị REST POLLING khi WebSocket im nhưng status và marker REST vẫn được cập nhật; chỉ giảm độ sáng HUD khi nguồn dữ liệu đã quá hạn.
- Theo dõi thời điểm bắt đầu request /me theo từng field để response chậm và response thiếu field không kéo stats về baseline trước frame live.
- Bỏ response hoàn thành sau cancellation/timeout; phục hồi online từ /me khi frame hasDino=false đã cũ.
- Thêm probe session 120 giây đếm thay đổi stats/vị trí và số frame WS, không ghi token hoặc tọa độ.

## 1.7.0

- Khi WebSocket stale hoặc reconnecting, stats, Growth, marker người chơi và chữ số chỉ giảm nhẹ còn opacity 0.7; chỉ dẫn đường vẫn sáng.
- Xác minh trực tiếp cho thấy `/ows` bắt tay thành công nhưng có thể không phát frame trong hơn 15 giây dù người chơi online; bỏ watchdog dựa trên thời gian im lặng để không tự phá socket còn sống và lặp `RECONNECTING` vô hạn.
- Bật keep-alive WebSocket 10 giây; chỉ reconnect khi handshake, socket hoặc mạng thực sự báo lỗi. Sau khi bắt tay lại thành công, giao diện thoát trạng thái reconnect ngay và dùng REST fallback cho tới frame live kế tiếp.
- Trạng thái request `/me`, `/map`, `/markers` nay được ưu tiên hiển thị; lúc rảnh mới hiện `WS · DATA STALE · REST FALLBACK`, không còn bị `RECONNECTING` che toàn bộ hoạt động fallback.
- Khi socket lỗi, đánh thức ngay poller `/map` và `/markers` để lấy vị trí fallback; tín hiệu được coalesced và vẫn qua gate single-flight nên không tạo request chồng hoặc dồn hàng.
- Sửa self-marker của `/map` không ghi nhận revision vị trí mới ở server thường; khi WS stale/reconnecting và REST thực sự có tọa độ mới, map nay chuyển sang vị trí REST ngay.
- Đổi phiên bản phát hành sang 1.7.0.

## 1.6.15

- Xác minh trực tiếp bằng phiên IslePilot thật: `/me`, `/map` và `/markers` đều trả dữ liệu hợp lệ ở cả 5/5 lượt; map có đủ 70 POI, marker và calibration.
- Bổ sung regression test bắt buộc `/me`, `/map` và `/markers` tiếp tục polling độc lập khi WebSocket đang kết nối và đã phát frame `live`; WS không thể làm dừng luồng REST.
- Thêm công cụ probe cục bộ có che token để phân biệt rõ lỗi DNS/TLS, REST decode và WebSocket không phát frame trong các lần chẩn đoán sau.

## 1.6.14

- Giảm thời gian phát hiện WebSocket im từ 12 giây xuống 5 giây, nên trạng thái stale chỉ kéo dài khoảng 1 giây trước khi app chủ động nối lại.
- Rút timeout handshake từ 5 giây xuống 3 giây và tăng tốc backoff thành `0,25 → 0,5 → 1 → 2 → 4 → 8 giây`; lần phục hồi đầu gần như tức thì nhưng vẫn tránh reconnect dồn dập khi server offline lâu.
- Backoff vẫn chỉ reset sau khi nhận được frame `live` thật, tránh trường hợp socket chỉ accept rồi im lặng tạo vòng reconnect nhanh vô hạn.

## 1.6.13

- Làm đường chỉ dẫn cyan luôn sáng rõ trên minimap, Alt+M và map F8; không giảm độ sáng khi WebSocket chuyển stale/reconnecting.
- Hiển thị khoảng cách theo mét ngay phía trên điểm dẫn đường và sau tên friend/group, đồng thời cập nhật lại khoảng cách mỗi khi vị trí hiện tại thay đổi.
- Đưa `WS · DATA STALE` và `WS · RECONNECTING…` sang đúng ô trạng thái request bên trái Activity để nhìn thấy ngay tình trạng realtime.

## 1.6.12

- Chặn chạy đồng thời nhiều bản IsleLiveMap trên cùng máy; tránh hai tiến trình dùng chung overlay token tranh kết nối `/ows`, khiến WebSocket lúc live lúc stale và map đứng ngẫu nhiên.
- Giữ watchdog cho socket đã kết nối nhưng ngừng phát frame: tự đóng và nối lại theo backoff, giữ vị trí hợp lệ gần nhất trong lúc chờ và nhận ngay tọa độ/hướng mới khi realtime hồi phục.
- Bổ sung regression test cho toàn bộ chuỗi `live → socket im lặng → reconnect → live`, cùng test khóa single-instance và chạy lại toàn bộ bộ kiểm thử trước khi đóng gói.

## 1.6.11

- Thay ảnh catalog bên ngoài bằng thumbnail dựng từ chính model 3D `/cdn/skinviewer` mà IslePilot Garage sử dụng; Pachy, Triceratops và 20 loài khác nay đúng silhouette/pose trên web.
- Thumbnail nền trong suốt được đóng gói local nên Garage mở nhanh, không còn ảnh phong cảnh, ảnh sai loài hoặc mất ảnh khi website ảnh bên ngoài lỗi.
- Card phủ màu body từ palette Garage thật lên model và vẫn giữ toàn bộ dải màu skin bên dưới. Baryonyx dùng fallback tối giản vì IslePilot hiện chưa cung cấp model 3D cho loài này.

## 1.6.10

- Garage đọc `settings.liveSwap` từ đúng server hiện tại: server bật Live Swap hiển thị `ĐỔI SANG`, server tắt Live Swap hiển thị `LẤY RA`.
- Thêm nhãn chế độ ngay trên số Dino và đồng bộ nội dung xác nhận/kết quả theo đúng thao tác của server, không còn dùng chung câu “lấy ra”.
- Đóng F8 chỉ đóng giao diện và dừng tác vụ local; app không tự gửi lệnh `cancel`. Chỉ nút `HỦY` trong tiến trình cất Dino mới yêu cầu server hủy.

## 1.6.9

- Garage F8 nay cho phép cất Dino đang chơi và lấy/đổi Dino đã lưu qua API overlay chính thức; mọi thao tác đều có bước xác nhận, đếm ngược và theo dõi trạng thái server.
- Sửa Growth/Health/Food/Water/Stamina bị hiển thị quá thấp: dữ liệu Garage dạng tỷ lệ `0..1` được đổi đúng sang phần trăm `0..100`.
- Thêm ảnh minh họa đúng loài cho card, giữ bảng màu skin từ dữ liệu thật và fallback gọn khi nguồn chưa có ảnh của loài đó.
- Khi đóng F8 giữa lúc đang cất Dino, app dừng đếm ngược và gửi lệnh hủy tối đa 5 giây trước khi đóng, tránh để thao tác dở dang trên server.

## 1.6.8

- Thêm tab `GARAGE` trong cẩm nang F8 cho phiên IslePilot; dữ liệu chỉ tải khi mở tab và có nút làm mới riêng.
- Kết nối endpoint chính thức `/api/overlay/garage` bằng overlay token hiện có, không bắt đăng nhập Steam thêm và không gửi thao tác Restore/Rename/Delete.
- Card Garage hiển thị song ngữ species/tên, giới tính, Growth, Health, Food, Water, Stamina, Prime Elder, thời điểm park, trạng thái mutation và bảng màu skin thật.
- Có đầy đủ trạng thái loading, garage trống, token hết hạn, server chậm/lỗi; tab tự ẩn với nguồn không dùng IslePilot.

## 1.6.7

- Sửa hướng minimap bị đứng khi WebSocket đã stale: yaw WS chỉ được ưu tiên trong 4 giây sau frame hướng mới, sau đó tự dùng yaw mới nhất từ self-marker REST.
- Khi WS phát frame trở lại, hướng realtime lập tức giành lại quyền mà không cần vị trí X/Y phải thay đổi; vị trí vẫn giữ cơ chế chống kéo lùi độc lập.
- Xác minh `/map` hiện trả calibration, 70 POI/polygon, categories và markers; `/markers` SBTC trả self/group marker kèm yaw để làm fallback khi WS tạm im lặng.

## 1.6.6

- WebSocket mở ngay bằng tên Steam đã lưu, không còn phải chờ bootstrap `/me`; thêm timeout handshake 5 giây và watchdog 12 giây cho socket mở nhưng không có frame live.
- Chỉ reset reconnect backoff sau frame live đầu tiên; kết nối accept rồi im lặng/đóng liên tục sẽ backoff đúng thay vì reconnect dày và dễ chạm rate-limit.
- WS vẫn ưu tiên khi X/Y thực sự thay đổi; nếu tọa độ WS đứng quá 6 giây nhưng self-marker `/markers` có vị trí mới hơn thì dùng marker làm fallback, trong khi yaw WS vẫn cập nhật bình thường.
- Thêm trạng thái request ở sát mép trái footer Activity (`REQUESTING /MAP…`, `RETRYING /ME…`); `SYNC` giữ sát mép phải theo bố cục space-between.

## 1.6.5

- Dùng endpoint `/api/p/sbtcisland/map/markers` trực tiếp trên `islepilot.eu`; đo 10 lượt cho trung vị khoảng 246 ms, ổn định hơn domain website SBTC mới.
- Tách `/markers` thành luồng single-flight độc lập mỗi 5 giây, chạy song song với `/map` 2 giây và `/me` 5 giây nên request chậm không chặn nhau hoặc dồn request.
- Giữ marker SBTC hợp lệ gần nhất khi endpoint tạm lỗi; dữ liệu polygon/calibration vẫn tiếp tục lấy độc lập từ `/map`.

## 1.6.4

- Cho phép giữ `Alt + =` hoặc `Alt + -` để zoom liên tục theo tốc độ lặp bàn phím, thay vì phải nhấn từng lần.
- Chỉ bật key-repeat cho hai hotkey zoom; `Alt + M`, `F8` và phím Settings vẫn giữ chống lặp để không tự bật/tắt nhiều lần.

## 1.6.3

- Khi WebSocket vị trí bị stale, giữ nguyên tọa độ WebSocket hợp lệ gần nhất thay vì fallback về self-marker `/map` cũ và kéo bản đồ lùi lại.
- `/map` chỉ cấp vị trí ban đầu khi phiên hiện tại chưa từng nhận tọa độ WebSocket; khi realtime hoạt động lại, tọa độ mới tiếp tục cập nhật bình thường.
- Stats/Growth vẫn fallback độc lập sang `/me` khi stale, không ảnh hưởng quyền ưu tiên của vị trí.

## 1.6.2

- Tăng tốc vị trí hiện tại bằng cách ưu tiên tọa độ WebSocket còn tươi, cùng nhịp realtime với hướng nhìn, thay vì luôn chờ self-marker `/map` chu kỳ 2 giây.
- Response `/map` tới sau không còn kéo vị trí realtime về marker REST cũ; app giữ một nguồn liên tục để tránh giật tới-lùi.
- Chỉ fallback sang self-marker `/map` khi WebSocket chưa có tọa độ hoặc tọa độ realtime đã stale quá thời hạn.

## 1.6.1

- Tách lại đúng hai hàng đợi độc lập: `/map` tuần tự với `/map`, `/me` tuần tự với `/me`, nhưng hai loại request được phép chạy song song.
- Mỗi endpoint vẫn single-flight, timeout/retry trước khi gửi lượt kế tiếp nên không có request cùng loại chạy chồng hoặc bị dồn queue.
- `/map` chậm hoặc treo không còn chặn status `/me`, và `/me` chậm cũng không làm trễ lần tải map ban đầu.

## 1.6.0

- Tuần tự hóa toàn bộ REST request `/me` và `/map` bằng một hàng đợi global single-flight: request trước phải hoàn tất, timeout hoặc retry xong thì request kế tiếp mới được gửi.
- Không còn trường hợp một request status và một request map chạy song song; các vòng poll chờ tại hàng đợi nên không dồn hoặc phát sinh quá nhiều request ra network.
- Khi một endpoint timeout, khóa luôn được nhả để endpoint còn lại tiếp tục đồng bộ ở lượt kế tiếp.

## 1.5.15

- Sửa stats/Growth/vitals thỉnh thoảng nhảy về dữ liệu cũ khi response `/me` chậm hoàn tất sau một frame WebSocket mới hơn.
- Mỗi trường realtime còn tươi nay luôn được ưu tiên; `/me` chỉ fallback khi trường đó chưa có hoặc đã stale, nên response REST cache không còn làm thanh stats giật tới-lùi.
- Áp dụng cùng quy tắc cho tiến độ Prime và vẫn giữ fallback status độc lập khi WebSocket tạm mất dữ liệu.

## 1.5.14

- Live Map nay cập nhật với mọi thay đổi tọa độ X/Y, kể cả khi người chơi chỉ di chuyển một khoảng rất nhỏ.
- Chỉ giữ cache khi cả X và Y giống hệt response trước; bỏ toàn bộ ngưỡng khoảng cách từng khiến vị trí gần bị bỏ qua.

## 1.5.13

- Giảm thời gian tải Live Map ban đầu bằng cách gửi `/map` ngay song song với `/me`, thay vì bắt map chờ status hoàn tất rồi mới bắt đầu.
- WebSocket vẫn đợi bootstrap danh tính để giữ đúng persona, nhưng độ trễ `/me` không còn cộng dồn vào thời gian xuất hiện map/polygon/marker.
- Kiểm tra thực tế cho thấy độ trễ chủ yếu đến từ response IslePilot (có request `/map` gần 3,6 giây), không phải DNS/TLS hay decode ảnh local.

- Gỡ hoàn toàn phép chiếu calibration động của 1.5.11 vì Asset Location và dữ liệu marker server dùng thứ tự trục khác nhau, gây điểm Ctrl+V lệch rất xa.
- Khôi phục phép chiếu Gateway ổn định: giá trị Asset Location thứ nhất đi theo trục dọc, giá trị thứ hai đi theo trục ngang.
- Hiệu chỉnh lại hệ số bằng ba điểm chuẩn đã xác minh trên ảnh Gateway 7800 x 7817 để tăng độ chính xác so với các offset làm tròn cũ.

- Sửa Ctrl+V/Copy Asset chỉ sai điểm do dùng phép chiếu Gateway cố định trong khi marker Live Map dùng calibration động của server.
- Truyền calibration `/map` hiện tại vào minimap, bản đồ `Alt + M` và trang Bản đồ F8; điểm paste nay dùng cùng hệ tọa độ với marker người chơi và polygon.
- Chỉ fallback về phép chiếu Gateway bundled khi server chưa cung cấp calibration.

- Tách hàng đợi `/map` và `/me`: Live Map lỗi, chậm hoặc timeout không còn chặn việc fetch status/Growth/vitals mỗi 5 giây.
- Mỗi endpoint vẫn chỉ chạy tối đa một request và tự retry độc lập, không tạo request chồng hoặc tích tụ timer tick.

- Điều chỉnh đúng ưu tiên Copy Asset: ghi lại revision và tọa độ `/map` tại lúc copy; chỉ trả quyền auto-detect khi một response `/map` mới hơn thực sự tới và vị trí mới khác vị trí cũ.
- Snapshot status/WebSocket render lại cùng map revision không còn được xem nhầm là cập nhật Live Map, nên không thể kéo marker về dữ liệu cache.
- Không yêu cầu tọa độ Live Map mới phải nằm gần tọa độ copy; bất kỳ vị trí mới hợp lệ nào từ revision kế tiếp đều được nhận ngay.

- Sửa regression khiến marker bản đồ nhảy tới–lùi do luân phiên giữa tọa độ WebSocket và self-marker `/map` có độ trễ khác nhau.
- Khi Live Map có self-marker, vị trí và hướng nay luôn lấy ổn định từ `/map` theo chu kỳ 2 giây; tọa độ WebSocket chỉ dùng dự phòng nếu server không trả self-marker.
- Status/vitals vẫn nhận realtime qua WebSocket và tiếp tục fallback sang `/me` mới nhất khi cần.
- Chọn self-marker khớp SteamID trước cờ `Self`, tránh trường hợp response duplicate/sai cờ làm bản đồ nhảy sang vị trí người khác.

- Không còn bắt WebSocket status phải chờ endpoint bản đồ hoàn tất: realtime kết nối ngay sau khi tải danh tính, còn Live Map fetch độc lập ngay sau đó.
- Sửa lỗi dữ liệu WebSocket cũ giữ Growth/HP/Food/Water/Stamina vô thời hạn; từng chỉ số nay chọn nguồn mới nhất giữa realtime và `/me`.
- Khi tọa độ realtime quá hạn 4 giây nhưng `/map` vẫn có self marker mới, vị trí tự chuyển sang dữ liệu Live Map thay vì đứng ở tọa độ WebSocket cũ.
- Giữ chu kỳ Live Map 2 giây, status 5 giây, timeout/retry và cơ chế chống dồn request như trước.

- Khi bản đồ `Alt + M` hoặc trang Bản đồ trong F8 đang mở, nhấn `Ctrl + V` với tọa độ Location hợp lệ sẽ đặt ngay điểm đến và vẽ đường chỉ dẫn; dữ liệu clipboard không hợp lệ không làm mất điểm đã chọn.
- Tách thao tác dán điểm đến khỏi Copy Asset nền để tọa độ đích không kéo marker người chơi tới sai vị trí.
- Đồng bộ marker bạn bè/group từ minimap lên cả bản đồ lớn và bản đồ F8, gồm tên, màu phân biệt group/friend và hướng nhìn khi server cung cấp.

- Bỏ nền tròn cam của icon Food; giữ biểu tượng thịt/xương fill cam trên nền trong suốt để đồng bộ với các icon Activity còn lại.

- Sửa ô chọn loài hiển thị chuỗi debug `SpeciesMutationGuide {...}`: template nay bind trực tiếp `SelectedItem.Name`, chữ trắng và tự cắt gọn khi thiếu chiều rộng.
- Ẩn scrollbar bên phải danh sách loài nhưng vẫn giữ cuộn dọc bằng con lăn/touchpad.
- Vẽ lại icon Food theo mẫu badge tròn cam với biểu tượng thịt và xương fill trắng rõ ràng ở bên trong.

- Thay icon Food trong bảng Activity bằng miếng steak vector rõ ràng, có phần thịt, viền mỡ và xương; giữ nét sắc ở mọi mức scale và không dùng emoji hệ thống.

- Thay dropdown loài mặc định của Windows bằng template dark tùy chỉnh hoàn chỉnh, gồm nút chọn, mũi tên vector, popup, item hover/selected và scrollbar đồng bộ.
- Hiển thị tên Mutation theo dạng song ngữ `English (Tiếng Việt)` để khớp tên thật trong game nhưng vẫn dễ đọc giải thích.
- Chuyển cẩm nang F8 sang phong cách minimal đen/xám bán trong suốt, bỏ outer resize border và thay toàn bộ emoji điều hướng bằng vector Path/SVG-style.
- Trang Tổng quan F8 nay hiển thị trực tiếp Dino hiện tại: loài, tên, server, Growth, HP, Stamina, Food, Water và thời gian sync; tự chuyển trạng thái khi chưa vào server.
- Thêm mục Help cho `F8`, `Alt + M`, `Alt + = / -` và `Ctrl + Shift + O`.
- Map trong F8 dùng trực tiếp cùng bộ điều khiển với map `Alt + M`: chung texture, polygon, label, tọa độ, điểm đến, đường chỉ dẫn, zoom và pan.
- Bỏ hoàn toàn nét viền của cửa sổ map `Alt + M`, chỉ giữ hình dạng bo góc native.

- Căn bản đồ `Alt + M` lại chính giữa đúng màn hình chứa overlay mỗi lần mở, đồng thời tự thu vừa vùng làm việc trên màn hình nhỏ.
- Bỏ non-client resize border màu trắng của Windows và áp dụng cửa sổ bo góc native với viền xanh tối theo thiết kế hiện tại.
- Tăng tốc mở map bằng cách dùng lại texture WebP đã giải mã của minimap, giữ cửa sổ/polygon/label trong bộ nhớ và dùng `Hide/Show` thay vì hủy rồi dựng lại.
- Rút hiệu ứng mở còn 90 ms và bỏ fade opacity, để nội dung xuất hiện ngay khi nhấn `Alt + M` nhưng vẫn giữ chuyển động nhẹ.

- Thêm cẩm nang trong game mở/đóng bằng phím tắt toàn cục `F8`, có giao diện đồng bộ với HUD Isle Live Map và hiệu ứng chuyển trang mượt.
- Thêm điều hướng ba trang: Tổng quan, Bản đồ đảo Gateway và Tiến hóa Mutation.
- Thêm bộ chọn đủ 23 loài Evrima; tự chọn loài đang chơi khi mở, đồng thời cho phép tra cứu các loài khác.
- Thêm ba hướng build cho từng loài: Sinh tồn, Combat và Di chuyển; mỗi hướng có ba Mutation ưu tiên, hiệu ứng, lý do và điều kiện/tag liên quan.
- Dữ liệu Mutation đối chiếu theo danh mục theisle.info cập nhật 28/05/2026 và có cảnh báo pool/giá trị có thể được server tùy chỉnh.

- Đổi cửa sổ `Alt + M` sang chế độ chỉ hiển thị bản đồ, không còn header/footer hay bảng hướng dẫn che nội dung.
- Thêm zoom mượt bằng con lăn chuột (100–400%), kéo chuột trái để pan và click ngắn để chọn điểm đến; click phải vẫn xóa điểm đến.
- Hiển thị tên đầy đủ của các vùng/địa điểm đang có trong dữ liệu polygon và làm mượt hiệu ứng mở map, zoom, vị trí người chơi cùng đầu đường chỉ dẫn.
- Chặn key-repeat ở hotkey Windows và thêm debounce an toàn, sửa lỗi `Alt + M` đôi lúc vừa mở đã tự đóng.

- Thêm phím tắt toàn cục `Alt + M` để mở/đóng bản đồ Gateway kích thước lớn.
- Cho phép click trái chọn điểm đến, click phải hoặc nút `XÓA ĐIỂM ĐẾN` để hủy; vị trí hiện tại và điểm đến được đánh dấu rõ ràng.
- Đồng bộ đường chỉ dẫn từ vị trí hiện tại lên cả bản đồ lớn và minimap; đường đi tiếp tục cập nhật theo vị trí, zoom, pan và chế độ xoay map.
- Giữ polygon vùng trên bản đồ lớn theo đúng thứ tự hiển thị: nền map → polygon → đường chỉ dẫn → marker.
- Đường hiện tại là đường chỉ hướng thẳng chính xác theo tọa độ, không giả lập đường đi bộ khi chưa có dữ liệu đường/địa hình Gateway.

- Bỏ hai nút website riêng DinoVietNam/Premium khỏi Home; cả hai tiếp tục được tự nhận qua một phiên IslePilot chung với đầy đủ stats, polygon và marker.

- Thêm kết nối trực tiếp DinoVietNam (`dinovietnam.islepilot.eu`) bên cạnh DinoVietNam Premium.
- Tổng quát hóa Gateway polygon/POI cho mọi nguồn IslePilot: ưu tiên POI live của đúng host/server, dùng catalog Gateway offline khi API chưa có dữ liệu.
- Chuyển marker friend/group của mọi server IslePilot sang cùng lớp map, đồng thời gắn đúng server, map projection và stats 5 giây cho nguồn website riêng.

- Thêm nút kết nối trực tiếp DinoVietNam Premium (`dinovietnampremium.islepilot.eu`) trên Home, dùng đúng phiên `islepilot_player`, slug và API IslePilot riêng của server.

- Điều chỉnh chu kỳ REST Live Map thành 2 giây; realtime WebSocket, hàng đợi single-flight và retry timeout giữ nguyên.

- Điều chỉnh chu kỳ đồng bộ Growth/profile thành 5 giây để cân bằng độ trễ và tải request; timeout vẫn là 5 giây và không chạy request chồng nhau.

- Tăng tốc đồng bộ Growth từ 10 giây xuống 5 giây; dữ liệu Growth từ realtime vẫn được áp dụng ngay khi nhận được.
- Tuần tự hóa toàn bộ request REST của IslePilot để mỗi thời điểm chỉ có một request đang chạy, không tích lũy request khi mạng chậm.
- Thêm timeout 5 giây và tự retry một lần sau 250 ms; nếu vẫn lỗi, vòng polling tiếp tục thử lại ở chu kỳ sau.

- Tăng nhịp Live Map lên mỗi giây; tọa độ realtime đã nhận luôn được ưu tiên hơn marker REST cũ khi kết nối tạm stale.
- Cache vị trí X/Y không đổi để tránh render và pan map thừa, trong khi hướng nhìn/yaw vẫn cập nhật độc lập.
- Giữ Copy Asset cho tới khi Live Map thật sự có tọa độ mới, không còn bị response cũ kéo map về vị trí trước đó.

- Chuyển tài nguyên nền Gateway trở lại WebP để tương thích với chế độ Discord stream-only-app.
- Ngăn overlay cướp focus ngay lúc mở, tránh làm The Isle mất fullscreen và khiến Discord application stream bị dừng.
- Cắt nền sáng/opaque nối với mép icon vùng SBTC, đồng thời chuẩn hóa alpha WebP để biểu tượng hiển thị trong suốt trên map.
- Khôi phục hành vi cập nhật vị trí và ưu tiên Copy Location như bản 1.2.5.
- Tách profile đăng nhập Steam/IslePilot, tự xóa phiên Steam cũ khi cần đăng nhập lại và thêm nút ĐỔI TÀI KHOẢN.
- Thêm vault nhiều tài khoản Steam được mã hóa, bộ chọn tài khoản, thêm/xóa riêng từng tài khoản và hiển thị `SteamID (Tên Steam)`; làm mới thẻ nguồn bằng badge/icon rõ ràng.
- Thiết kế lại dropdown tài khoản theo dark theme; tái sử dụng WebView2 environment và chỉ xóa cookie thay vì toàn bộ cache/site storage để đăng nhập nhanh, ít lag hơn.
- Dùng icon SBTC PNG alpha chuẩn để bỏ nền đen lỗi; tách polygon và icon/label thành hai layer để icon luôn trên các vùng màu nhưng dưới marker/chỉ dẫn người chơi.
- Làm mượt chuyển động map khi Copy Asset hoặc live location cập nhật bằng pan animation đồng bộ cho nền, polygon, icon và marker đồng đội; marker người dùng giữ cố định ở tâm.
- Giữ polygon SBTC gần nhất khi API trả response rỗng/tạm lỗi và luôn tiếp tục fetch map; chỉ hết phiên đăng nhập hoặc đóng app mới dừng session.

- Gỡ hoàn toàn tính năng Team, relay và marker đồng đội.
- Gỡ cơ chế tự kiểm tra/tải cập nhật từ GitHub; app chỉ cập nhật khi người dùng chủ động cài bản mới.
- Thêm bảng nhiệm vụ Prime tự nhận telemetry, mặc định tắt và có Việt hóa trạng thái hoàn thành.
- Sửa vị trí nhãn SYNC trong Activity và đặt bảng Prime cùng chiều rộng với Activity.
- Làm nền HUD trong suốt hơn và bỏ hiệu ứng panel có thể gây nền đen khi resize hoặc đổi DPI.
- Bỏ pattern tam giác dạng pixel khỏi nền HUD, thay bằng nền trong suốt phẳng.
- Thêm lớp Sanctuary, Migration/MMZ và Patrol Zone tự động trên minimap khi chơi server SBTC, kèm dữ liệu Gateway offline khi API không trả polygon.
- Gộp các nhãn zone trùng tên và cùng loại thành một nhãn, trong khi vẫn giữ đầy đủ đường viền của từng khu vực.
- Đồng bộ cách vẽ zone SBTC với IslePilot: dùng shape, size, màu và icon live từ API; hiển thị đủ 6 Hunting/Prey circle và ẩn lớp Locations mặc định để tránh nhãn chồng.
- Hiển thị friend và đồng đội cùng group trên minimap SBTC, có tên phía trên marker và màu riêng cho từng loại.
- Thêm Alt + = để zoom in và Alt + - để zoom out ngay cả khi overlay đang click-through.
- Chuẩn hóa tên zone và gộp marker/nhãn trùng để không bị lặp do khác chữ hoa hoặc khoảng trắng.
- Đưa bốn hướng B/Đ/N/T vào bên trong minimap và bỏ hoàn toàn nền của chữ chỉ hướng.
- Bỏ toàn bộ đường viền ngoài của minimap ở cả kiểu Circle và Square.
- Ẩn dòng hướng nhìn ở đáy map; sửa font chữ Đ và thêm outline đen cho B/Đ/N/T mà không dùng nền.
- Đổi chữ phương hướng sang Segoe UI Condensed Bold, tăng nhẹ độ dày và kích thước giống giao diện tham khảo.
- Bỏ nền đen của nhãn biểu tượng Sanctuary/Migration và các zone trên map; giữ icon WebP có alpha trong suốt.
- Giảm chữ phương hướng B/Đ/N/T từ 13.5px xuống 11.5px.
- Tăng tốc vị trí friend/group bằng cách giảm map refresh từ 15 giây xuống 3 giây và loại bỏ độ trễ cộng dồn của thời gian request.
- Cải thiện minimap, cập nhật vị trí, map rotate, auto detect/copy tọa độ và độ rõ khi thu nhỏ HUD.
