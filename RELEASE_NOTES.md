# Isle Live Map 1.9.8

- Chia sẻ vị trí bạn bè chỉ với Npcap, kể cả khi web không có dữ liệu Dino hoặc server không hỗ trợ web.
- Gắn IP/cổng server của luồng game vào tọa độ Npcap. Hai endpoint khác nhau vẫn tách riêng dù trùng tên server; dữ liệu hết hạn hoặc ngắt capture vẫn tự ẩn vị trí.
- Bổ sung tên đầy đủ SDVN #3 X3 Grow vào danh sách tên tương đương.

# Isle Live Map 1.9.7

- Chuẩn hóa tên/mã SDVN #3 và SBTC khi so sánh server của bạn bè, giữ các server khác nhau tách riêng.
- Dùng server thực tế từ telemetry để chia sẻ vị trí, không suy ra server từ dropdown nguồn dữ liệu.
- Làm rõ hỗ trợ tên tiếng Việt có dấu; đổi tên giữ nguyên danh tính, bạn bè, mã kết bạn và mã khôi phục.
- Kiểm tra WebSocket trực tiếp cho tên server khác định dạng và tên bạn bè đổi khi đang online.

# Isle Live Map 1.9.6

- Chuyển API bạn bè sang `southtampanailsfl.com/api/islemap/`, giữ danh tính và mã khôi phục hiện có. Backend PHP chạy trong Docker; tài khoản và quan hệ lưu ở SQLite ngoài thư mục web.
- Vị trí bạn bè dùng một kết nối WebSocket thay cho HTTP polling mỗi giây. Gửi khi dữ liệu đổi, tối đa mỗi giây; đứng yên heartbeat mỗi 5 giây. Server gom cập nhật và chỉ gửi tới bạn đã chấp thuận cùng server/bản đồ.
- Vị trí hết hạn sau 10 giây; tắt chia sẻ, đổi server, hủy kết bạn hoặc khôi phục tài khoản cập nhật quyền ngay. Mạng lỗi tự kết nối lại có backoff; máy nhận chậm không tích lũy tọa độ cũ.
- API Vercel chuyển tiếp request của client cũ sang backend mới, giữ chữ ký và tài khoản; client cũ vẫn tiêu thụ request Vercel tới khi nâng cấp.

# Isle Live Map 1.9.5

- Bạn bè hiển thị tên kèm khoảng cách, loài ở dòng dưới; bỏ ONLINE và dòng trạng thái lặp. Danh sách dùng thẻ rõ ràng, cập nhật từng nhãn thay vì dựng lại toàn bộ danh sách mỗi giây.
- Điểm bạn bè trên minimap, map lớn và F8 thay đổi cùng thiết lập kích thước điểm vị trí. Kill Feed rộng bằng Activity và cùng tỷ lệ khi resize.
- Minimap xem toàn đảo tự ẩn tên vùng/địa danh để tránh chồng chữ, giảm kích thước AI và dùng nhãn bạn bè gọn; zoom vào sẽ hiện tên trở lại. Vùng tím/vàng và nước vẫn giữ theo bộ lọc.
- API đọc tối đa 500 bạn bè trong một MGET. Redis chỉ giữ một vị trí mới nhất mỗi tài khoản trong 10 giây, nonce được gom theo tài khoản với hạn 120 giây; bộ đếm giới hạn hết hạn sau 60 giây hoặc tối đa 1 giờ. MongoDB giữ danh tính và quan hệ bạn bè lâu dài.
- Kiểm tra danh sách 500 bạn bè, quyền xem vị trí, dữ liệu hết hạn, chống replay, khoảng cách và hai mức zoom. Gói Redis free vẫn có giới hạn kết nối, tốc độ và băng thông; không cam kết 500 người đồng thời ở chu kỳ 1 giây.

# Isle Live Map 1.9.4

- Gửi lời mời bằng tên duy nhất hoặc mã kết bạn, có lựa chọn Tên/Mã để tránh nhầm khi tên trông giống mã. Tên không phân biệt hoa/thường và được chuẩn hóa Unicode.
- Ràng buộc tên duy nhất ở MongoDB cho cả tạo tài khoản và đổi tên; tên trùng báo lỗi, không thay đổi tài khoản hiện có. Tài khoản cũ giữ ID/bạn bè; tên trùng trước đây được thêm hậu tố ID.
- Chia sẻ vị trí mặc định bật mỗi lần mở app; vẫn cho tắt trong F8, khi tắt không gửi tọa độ. Chỉ bạn đã chấp thuận cùng server/bản đồ được nhận vị trí.

# Isle Live Map 1.9.3

- Thêm F8 → Bạn bè: bắt đặt tên khi tạo tài khoản, mã kết bạn, lời mời cần chấp thuận, từ chối/hủy lời mời và hủy kết bạn.
- Chia sẻ vị trí mặc định tắt mỗi lần khởi động. Khi tắt không gửi tọa độ lên API. Sync mỗi giây, không chồng request; mạng lỗi backoff và tự ẩn vị trí hết hạn 10 giây.
- Chỉ hiện vị trí bạn đã chấp thuận cùng server/bản đồ; nhãn trên map có tên, loài và ONLINE. Trạng thái online/offline có trong tab Bạn bè.
- Khóa ký thiết bị và mã khôi phục mã hóa Windows DPAPI trong AppData. Chuyển máy bằng mã khôi phục giữ nguyên tài khoản/bạn bè, thu hồi máy cũ và đổi mã dự phòng.
- API Vercel tại `wowie-theisle.vercel.app`, source ở `services/friends-api`: MongoDB lưu tài khoản/bạn bè, Redis lưu presence tạm; xác thực chữ ký, chống replay và giới hạn lời mời. Đã kiểm tra health và luồng online với tài khoản giả lập riêng.

# Isle Live Map 1.9.2

- Không khởi động lại bộ bắt packet chỉ vì cổng UDP phụ thay đổi, miễn cổng kết nối game đang dùng vẫn còn.
- Giữ khóa actor đã xác nhận khi packet movement tạm ngắt hoặc không đọc được handle; cùng handle sau khoảng im không làm mất số hiện tại / tối đa. Vẫn xóa khi có handle khác, đóng kênh hoặc reset luồng.
- Giữ mẫu đã xác nhận qua khoảng ngắt packet ngắn thay vì xóa sau 1,5 giây; luồng server im quá 15 giây sẽ xóa dữ liệu để tránh giữ số sau mất kết nối.
- Có regression test cho khoảng ngắt movement, handle không đọc được, max không gửi lại và mất kết nối dài; giữ kiểm tra đổi Dino / respawn và từ chối kết nối khác.

# Isle Live Map 1.9.1

- Tắt vùng sẽ ẩn cả tên vùng, kể cả nhãn riêng từ API, trên minimap, map lớn và map trong F8. Nước vẫn hiển thị.
- Thêm Kill Feed SBTC cạnh minimap với 5 bản ghi mới nhất; bật/tắt trong setting HUD hoặc F8, lưu lựa chọn và mặc định tắt. Khi bật cập nhật mỗi 5 giây, không chồng request; tắt sẽ dừng tải. Giữ dữ liệu cũ khi mạng lỗi và thử lại. Tab Kill Feed F8 cũng cập nhật mỗi 5 giây.

# Isle Live Map 1.9.0

- Đọc máu, food, nước và stamina hiện tại / tối đa từ packet; khi chưa đủ dữ liệu dùng phần trăm web, thiếu cả hai thì hiện `- / -`. Thanh phần trăm dài như bone, cặp số có cột riêng và tất cả số căn phải.
- Đóng gói sẵn vùng Patrol tím và Migration vàng, mặc định bật trên máy mới; vẫn có vùng khi API không trả dữ liệu. Nước luôn hiển thị; bộ lọc vùng và AI riêng.
- Tích hợp AI, bạn bè và kill feed SBTC; điều chỉnh kích thước icon theo zoom, kích thước điểm người chơi và lớp AI trên / dưới điểm.
- Tích hợp Studio skin và thiết kế đã lưu trên SBTC, sửa model 3D, gửi skin không hỏi xác nhận lần hai; ẩn Garage.
- Lưu server được chọn lần cuối và thêm Ctrl+R để xóa trail nhanh.
- Chuyển dòng phiên bản mới sang 1.9.0; giữ feed cập nhật đầy đủ cho bản cài.

# Isle Live Map 1.8.33

- Đóng gói sẵn vùng Patrol màu tím và Migration màu vàng trong DLL. Cài mới mặc định bật vùng; không cần file vùng từ máy khác hoặc chờ API. Có kiểm tra đọc cả hai catalogue trực tiếp từ tài nguyên đã biên dịch.
- Các dòng hiển thị phần trăm dùng thanh dài như bone; cặp số hiện tại / tối đa dùng thanh ngắn để chừa chỗ cho số. Số căn phải và chiều dài thanh không đổi theo số chữ số.

# Isle Live Map 1.8.32

- Cố định các thanh máu, stamina, food và nước cùng chiều dài trong mọi chế độ số hoặc phần trăm. Giữ thanh bone dài như trước và căn phải các số.

# Isle Live Map 1.8.31

- HUD có hai bố cục cố định theo từng dòng: phần trăm / chưa có dữ liệu dùng cột số 32 px để thanh dài hơn; hiện tại / tối đa dùng cột 64 px. Tất cả số căn phải cùng mép, chiều dài thanh không đổi theo số chữ số.
- Kiểm tra ảnh render cho phần trăm, cặp số, dữ liệu trộn, Dino nhỏ, HP lớn, food bằng 0 và chưa có dữ liệu; chọn bố cục theo từng dòng sau khi so sánh với cách dùng chung cột rộng cho cả bảng.

# Isle Live Map 1.8.30

- Khi chưa đủ cặp hiện tại/tối đa từ packet, dùng phần trăm web cho chỉ số đó; nếu chưa có dữ liệu web thì hiển thị `- / -`. Khi nhận đủ packet, tự chuyển sang cặp số thực; không suy đoán sức chứa hoặc lấy số tối đa của Dino cũ.
- Thu hẹp cột số cố định còn 64 px, căn số về sát thanh với khoảng cách 4 px. Các thanh vẫn giữ nguyên chiều dài khi số thay đổi.

# Isle Live Map 1.8.29

- Cố định cột số trong HUD ở 74 px để các thanh HP, food, nước, stamina và xương luôn có cùng chiều dài. Chuyển từ “Đang đồng bộ” sang số, đổi số chữ số hoặc cập nhật giá trị không còn làm thanh co giãn.

# Isle Live Map 1.8.28

- Khi mở overlay giữa phiên và mới nhận giá trị hiện tại, HUD báo “Đang đồng bộ” thay cho các cặp thiếu số tối đa như `87.5 / —`. Ẩn thanh chưa tính được phần trăm để tránh trông giống chỉ số bằng 0; tooltip vẫn cho biết giá trị hiện tại đã nhận.
- Khi packet gửi sức chứa, HUD tự hiện lại thanh và cặp hiện tại / tối đa. Không thay đổi bộ đọc packet hoặc suy đoán số tối đa trong thời gian chờ.

# Isle Live Map 1.8.27

- Đọc HP, food, nước và stamina hiện tại/tối đa từ khối thuộc tính Iris đã đối chiếu trên packet SBTC. Giải mã mask byte thưa và các cặp scalar thay cho suy đoán vị trí float; cập nhật từng phần giữ nguyên các trường game chưa gửi lại.
- Khóa đúng object handle từ RPC di chuyển của client rồi theo dõi subobject thuộc tính của Dino đó. Đổi con, respawn, đổi kết nối hoặc đóng kênh xóa dữ liệu cũ; hỗ trợ handle tăng từ 2 lên 3 byte. Không dùng khối KG không có object identity trên luồng Iris đã nhận diện.
- HUD và tổng quan F8 ưu tiên chỉ số packet. HP vẫn hiển thị số nguyên; food/nước/stamina hiển thị hiện tại / tối đa với một số lẻ. Food bằng 0 giữ đúng 0%; mẫu thiếu sức chứa hiển thị dấu —, không mượn số tối đa của Dino cũ trên web. Schema SBTC đang đối chiếu dùng mức nước mặc định 1000 nếu server chưa gửi sức chứa khác.
- Giữ sức chứa đã nhận khi growth/KG đạt plateau. Khối packet chưa nhận diện được bị bỏ qua; bật app giữa phiên có thể cần chờ game gửi lại sức chứa HP/food/stamina. Kiểm tra bằng replay packet respawn và capture trực tiếp; dữ liệu bắt mạng không nằm trong bản phân phối.

# Isle Live Map 1.8.26

- Giữ KG đã xác thực của Dino hiện tại khi khối lượng ngừng thay đổi và game không gửi lại khối thuộc tính. Mẫu thuộc đúng actor không còn hết hạn sau 120 giây; không khóa hoặc tăng KG theo mốc growth/prime.
- Mẫu ban đầu chưa xác định đúng actor vẫn hết hạn sau 120 giây. Đổi kênh Dino, đóng actor, mất nguồn/kết nối hoặc tắt Npcap vẫn bỏ số máu dùng KG cũ; phép tính tiếp tục dùng KG nguyên nhân % máu web.
- F8 báo đang giữ cân nặng cuối của Dino hiện tại khi mẫu không đổi lâu. Test plateau ba giờ, cập nhật % máu, nhận KG mới tăng/giảm, đổi actor và mẫu ban đầu quá hạn.

# Isle Live Map 1.8.25

- Bộ lọc VÙNG chỉ điều khiển các polygon patrol tím, migration vàng và sanctuary. Lớp nước uống, địa danh và tên nước luôn hiển thị; AI có bộ lọc riêng như trước.
- Sửa mất vùng khi endpoint vùng lỗi nhưng AI vẫn tải được: giữ bộ vùng đã tải thành công trong provider; nếu chưa có vùng server thì ghép polygon dự phòng với AI hiện tại. Dữ liệu AI không còn thay thế bộ polygon tím/vàng.
- Kiểm tra ảnh render khi bật/tắt VÙNG, khi chỉ có feed AI và khi feed vùng phục hồi trên minimap, map lớn và F8.

# Isle Live Map 1.8.24

- Giảm icon AI trên bản đồ lớn và F8: toàn đảo 18 px (trước 28 px), zoom 4x 38 px (trước 44 px), zoom 8x 48 px. Kiểm tra ảnh render cùng vị trí ở ba mức zoom, giữ kích thước AI trên minimap.
- Thêm hai nút lọc VÙNG và AI trên bản đồ lớn và bản đồ F8. Có thể bật riêng hoặc bật cả hai, đồng bộ với minimap và lưu khi mở lại; cập nhật feed không bật lại lớp đã ẩn.
- Sửa KG Ptera ở growth cao: trường ngữ cảnh đi kèm cân nặng có thể vượt 1,1, không phải phần trăm. Bỏ giới hạn sai, vẫn kiểm tra cặp ngữ cảnh và toàn bộ bản sao KG/sức chứa, từ chối ngữ cảnh dùng chính sức chứa. Replay packet Ptera 83% nhận 111,358 → 112,158 kg từ đúng actor; thêm test bằng các scalar số đã tách khỏi packet.
- Máu tiếp tục dùng phần nguyên KG nhân phần trăm máu từ web và hiển thị số nguyên.

# Isle Live Map 1.8.23

- Thêm chỉnh kích thước điểm vị trí từ 50% đến 200% trong F8 → Cài đặt và bảng settings nhỏ, đồng bộ tức thì trên minimap, bản đồ lớn và bản đồ F8.
- Cho chọn icon AI ở trên hoặc dưới điểm người chơi khi trùng vị trí. Mặc định giữ cách hiển thị hiện tại: kích thước 100%, AI ở dưới.
- Lưu và khôi phục hai tùy chọn khi mở lại app; cài đặt cũ vẫn giữ kích thước và thứ tự lớp mặc định. AI có lớp riêng trên minimap để đổi thứ tự mà không đưa nhãn vùng lên trên điểm người chơi.
- Kiểm tra bằng ảnh render WPF ở 50% / 100% / 200%, cả hai thứ tự lớp trên ba bản đồ; điểm vị trí không bị lệch, điểm đích giữ kích thước cũ.

# Isle Live Map 1.8.22

- Tăng kích thước icon AI trên bản đồ lớn và F8, bù cả tỷ lệ Viewbox và zoom. Icon có kích thước hiển thị 28 px ở 1x, 44 px ở 4x và 52 px ở 8x; kiểm tra bằng ảnh render WPF cùng vị trí trước/sau.
- Nhớ server được chọn lần cuối trong dropdown và khôi phục đúng nguồn khi mở overlay. Chỉ lưu ID server; phiên đăng nhập vẫn dùng kho hiện có.
- Sửa KG của Ptera: nhận thêm khối thuộc tính 16 scalar bên cạnh mẫu 20 scalar, vẫn kiểm tra đầy đủ ngữ cảnh, cặp máu và các bản sao cân nặng/sức chứa. Replay capture Ptera nhận đủ bốn mẫu 96,958 → 98,158 kg từ đúng channel Dino, không mất KG giữa các lần cập nhật.
- Thêm tab Kill Feed trong F8 từ `GET /api/boards/species`, hiển thị người hạ/nạn nhân, loài, growth và nguyên nhân. Lọc theo loài, cập nhật mỗi 30 giây khi tab đang mở, thử tối đa ba lần khi mạng lỗi và giữ dữ liệu đã tải kèm thời điểm cũ.

# Isle Live Map 1.8.21

- Vùng patrol từ feed SBTC dùng màu tím `#A78BFA` giống IslePilot trên minimap, bản đồ lớn và F8; giữ màu các loại vùng và AI khác.
- Sửa mất số máu giữa các lần cập nhật KG: capture Deino có hai khối thuộc tính cách nhau 80,130 giây, vượt giới hạn cũ 60 giây. Giữ mẫu đã xác thực tối đa 120 giây; vẫn bỏ mẫu khi mất luồng hoặc đổi actor, không làm mới tuổi mẫu từ dữ liệu không liên quan.
- Thêm test bằng các trường số đã tách khỏi packet Deino 70,891 / 73,960 / 3.275,415 kg và test tính máu liên tục qua khoảng cập nhật. Chưa có packet của máy trong report mới để xác nhận nguyên nhân tại máy đó.

# Isle Live Map 1.8.20

- Hiển thị AI đang sống trên SBTC Island từ `/api/ai_positions?platform=steam`, cập nhật mỗi 8 giây, dùng cùng tọa độ và biểu tượng động vật với bản đồ web. Có trên minimap, bản đồ lớn và bản đồ F8; giữ biểu tượng đúng kích thước khi phóng to.
- Loại tọa độ rỗng/không hợp lệ, xóa AI cũ khi nguồn mất hoặc lỗi; lỗi AI không ngắt chỉ số Dino, bạn bè hoặc vùng bản đồ.
- Sửa luồng HUD bỏ qua POI của SBTC để các vùng và AI từ server được truyền tới bản đồ thực tế.

# Isle Live Map 1.8.19

- Sửa overlay mất khỏi màn hình khi vị trí lưu nằm trong khoảng trống giữa nhiều màn hình hoặc màn hình đã ngắt kết nối; tự đưa cửa sổ về vùng làm việc thực.
- Không ghi đè vị trí đã lưu khi đóng cửa sổ chưa từng mở hoặc đang thu nhỏ. Các bài test bản đồ sử dụng file cấu hình riêng.

# Isle Live Map 1.8.18

- (local, chưa phát hành) Đối chiếu vùng với bản đồ web SBTC: vòng tròn dùng cùng 40 đỉnh của `map.js` và đúng khung ảnh thay vì chia bán kính cho toàn canvas; giữ điểm đặt nhãn gốc, không vẽ vòng tròn lớn cho tên khu vực/nước. Thay đổi ở bất kỳ đỉnh polygon nào cũng cập nhật bản đồ. Cache vùng tách theo provider để không dùng nhầm dữ liệu của phiên/host khác.
- (local, chưa phát hành) Sửa pin bạn bè trên minimap và bản đồ lớn: gộp tên và cờ friend/group/squad của Steam ID trùng như web, không bỏ pin thiếu tên (dùng loài làm nhãn), ưu tiên tọa độ pixel từ server theo `map.js`. Bạn bè có màu xanh; không nhận pin không được server đánh dấu trong các danh sách chia sẻ. Rút gọn danh sách cổng trong chẩn đoán F8 để đọc rõ trạng thái KG.
- (local, chưa phát hành) Sửa mục Đã lưu trong Skin: thêm nút TẢI LẠI riêng, số lượng và trạng thái tải/trống/lỗi; không để nút kiểm tra lệnh áp dụng che mất lỗi thư viện. Đọc cả thiết kế màu thường `/api/designs` và thiết kế tự lưu của Glitch Creator `/api/glitchcreator/designs`. Thiết kế thiếu bảng màu hoặc recipe không hỗ trợ vẫn hiện trong danh sách và có nút mở Studio; recipe không hợp lệ không làm mất cả kho.
- (local, chưa phát hành) Sửa các trường hợp có tọa độ nhưng thiếu KG để tính máu: đọc packet có một hoặc hai bit kết thúc, xác thực toàn bộ header trước khi cập nhật trạng thái, ghép các mảnh reliable đúng channel/sequence và giữ tạm khối cân nặng đến khi movement của client xác định đúng Dino. Không dùng mẫu của kết nối khác, mảnh thiếu hoặc mẫu quá hạn. F8 · Cài đặt hiển thị lý do đang chờ KG.
- (local, chưa phát hành) Gửi skin trực tiếp sau một lần bấm ÁP DỤNG, bỏ hộp xác nhận thứ hai; vẫn kiểm tra loài, quyền, phí và chống gửi lặp khi đang xử lý.
- (local, chưa phát hành) Sửa model 3D không hiện vì CSP chặn WebAssembly của Meshopt; kiểm chứng viewer đóng gói với GLB nén và chính CSP của ứng dụng.
- (local, chưa phát hành) Mục Đã lưu của Skin đọc toàn bộ thiết kế từ `GET /api/designs`, lưu mới bằng `POST /api/designs` giống studio SBTC. Lưu thiết kế không phụ thuộc skin genes hoặc Dino đang sống; lỗi kho đầy được hiển thị rõ.
- (local, chưa phát hành) Thêm nút XÓA ĐƯỜNG ĐI trên bản đồ lớn và bản đồ trong F8, cùng Ctrl+R khi game/overlay đang được chọn. Xóa đồng bộ đường đi trên mọi bản đồ; nhả phím tắt khi chuyển sang ứng dụng khác.
- (local, chưa phát hành) Sửa food và các chỉ số dưới hoặc bằng 1% bị nhân thành 100%: giữ đúng đơn vị phần trăm của nguồn web trên HUD và bảng F8.
- (local, chưa phát hành) Sửa đọc nhầm cân nặng của Dino nhỏ: chấp nhận KG dưới 5, yêu cầu đầy đủ ngữ cảnh thuộc tính trước nhóm dung lượng để loại các nhóm số giống cân nặng. Đối chiếu packet Ptera 2,84–3,59 kg; có test khi Dino mất máu hoặc máu về 0.
- (local, chưa phát hành) Bỏ ô KG khỏi bản đồ; cân nặng vẫn được đọc để tính máu:
  - Đọc khối thuộc tính có các bản sao cân nặng và sức chứa bằng nửa cân nặng khớp chính xác, thay cách chọn nhiều chuỗi float ổn định.
  - Chọn actor từ channel movement của client và ghép đúng hai chiều của cùng kết nối UDP. Nếu thiếu channel thì không dùng cân nặng từ luồng server.
  - Giữ mẫu tối đa 60 giây giữa các lần server gửi khối thuộc tính; mất luồng, đổi actor hoặc tắt NPCAP sẽ bỏ mẫu cũ.
  - Log `%LOCALAPPDATA%\IsleLiveMapData\npcap-weight.txt` chỉ ghi kg đã xác thực cùng channel, bit offset và tuổi mẫu. Có test bằng các trường số đã loại bỏ thông tin phiên từ capture Cera, cùng test chống đọc nhầm, lệch bit, sai actor và mẫu hết hạn.
- (local, chưa phát hành) Dòng máu trên HUD: lấy phần nguyên KG trước khi nhân phần trăm máu theo quy ước 1 kg = 1 HP tối đa (2,7 kg → 2 HP tối đa), hiển thị máu không có số lẻ. Cập nhật khi KG hoặc phần trăm máu thay đổi; thiếu hoặc hết hạn KG thì trở về phần trăm. Nguồn đã cung cấp HP hiện tại/tối đa giữ giá trị gốc. Phần trăm máu nhỏ hơn 1% được giữ đúng khi tính toán.
- (local, chưa phát hành) Ẩn Garage và tập trung Skin 3D trong F8 khi đăng nhập nguồn **SBTC Island**:
  - Chỉnh màu HEX, xem model SBTC bằng phiên Steam hiện tại, đọc và lưu thiết kế trên `/api/designs`, áp dụng màu thường qua `/api/studio/apply`. Màu recipe dùng RGB/255; bảng màu mặc định dùng RGB tuyến tính.
  - Kiểm tra quyền và skin genes trước khi gửi, hiện phí trên bảng skin; giữ mã yêu cầu khi mất phản hồi và kiểm tra delivery thay vì báo đã áp dụng từ phản hồi đang chờ. Skin glitch dùng studio trên web.
  - Xem trước dùng diffuse và mask vùng màu của studio SBTC, tải mask răng/miệng/móng theo hợp đồng server thay vì đoán kênh màu. Hỗ trợ model nén Meshopt, texture WebP/PNG và giữ bảng màu mới nhất trong khi model đang tải.
  - Sửa selector tài khoản tránh dereference credentials null của phiên website SBTC/Pandora.

# Isle Live Map 1.8.17

- Sửa lỗi **trạng thái không cập nhật khi phiên IslePilot đã bị thu hồi** (web vẫn cập nhật bình thường):
  - Token overlay bị xoay mỗi lần đăng nhập: token cũ bị chính host phát hành trả **410 Gone**, còn host khác trả 401. App cũ coi 410 là lỗi mạng nên vẫn mở overlay bằng token đã chết, cứ thử lại mãi mà không hiện chỉ số dino.
  - Nay 401 và 410 được xử lý là phiên hết hạn: ở Home app báo cần xác thực lại và mở cửa sổ đăng nhập Steam; trong phiên overlay, HUD báo `PHIÊN CẦN XÁC THỰC LẠI` thay vì đứng im. (403 vẫn là lỗi tạm thời vì Cloudflare trả 403 khi chặn bot — không được coi là hết phiên.)
  - Cửa sổ đăng nhập Steam giờ đọc trang lỗi của server và diễn giải thành câu: nếu server tắt tính năng overlay (`overlay_disabled`), app báo rõ "server đang tắt tính năng overlay" kèm gợi ý dùng Copy Asset + NPCAP, thay vì chỉ hiện một trang JSON khó hiểu.
- (local, chưa phát hành) Nguồn **SBTC Island** (sbtcislandd.com) cho live map + chỉ số, thay hẳn cho hướng giải mã từ packet (NPCAP) đã bỏ vì quá nhiều lỗi:
  - Đăng nhập Steam OpenID (`/auth/steam/login?next=/map`) trong cửa sổ WebView2 như các server khác; app lưu cookie phiên, tự phát hiện phiên hết hạn (site trả `signed_in:false` chứ không phải 401) và mời đăng nhập lại.
  - `/api/positions`: vị trí và hướng nhìn của bạn, kèm pin của bạn bè/group/squad khi họ bật chia sẻ. Mỗi entry có thể là toạ độ game (`ue_x`/`ue_y`) hoặc pixel bản đồ của site — app quy đổi bằng đúng calibration `gateway_v0217` của site (khớp với calibration Gateway sẵn có của app) rồi chiếu lên bản đồ như mọi nguồn khác.
  - `/api/live`: thẻ dino — loài, growth, các thanh máu/thể lực/đói/nước/oxy/máu, diet (carb/protein/lipid), chảy máu, nứt xương, prime/elder.
  - HUD lấy đủ những gì thẻ dino của site hiển thị: 4 thanh máu/thể lực/đói/nước, growth, diet (carb/protein/lipid), oxy, blood, nứt xương, chảy máu, giai đoạn sống (life stage) và prime/elder — phần ngoài 4 thanh gom vào một dòng điều kiện ngay dưới các thanh.
  - Sửa lỗi **Npcap không chạy với nguồn SBTC Island**: điều kiện khởi động Npcap chỉ tính server IslePilot nên với nguồn web mới thì Npcap không bật, marker chỉ nhích theo nhịp poll 4 giây. Nay mọi nguồn có live map (IslePilot, IslePilot hosted, SBTC Island) đều bật Npcap, nên vị trí vẫn mượt theo packet như trước và zone/player overlay của SBTC cũng chạy.
  - Dòng điều kiện trong HUD gọn lại: bỏ OXY, diet viết ngắn `C/P/L`, nứt xương không còn chiếm chữ mà hiện thành **icon xương** (chỉ hiện khi xương chưa lành, có tooltip % còn lại); panel chỉ số nới từ 248 lên 286 để chữ không bị xuống dòng giữa từ.
  - Bộ icon chỉ số đổi sang SVG tự chọn: tim (máu, xanh lá), **đùi gà** (thức ăn, cam), giọt nước (nước, xanh dương), xương (nứt xương, đỏ). Icon đường dẫn SVG dùng trực tiếp được trong WPF (`Path Data`), chỉ cần đổi sang quy tắc tô `F1` (nonzero) vì WPF mặc định EvenOdd còn SVG mặc định nonzero.
  - **Thêm tài khoản theo loại**: nút ＋ THÊM TÀI KHOẢN giờ hỏi bạn muốn thêm *IslePilot Network (Steam)* hay một *server dùng website riêng* (SBTC Island, EraGaming, PANDORA). Server web đăng nhập một lần rồi **xuất hiện trong danh sách tài khoản** kèm dòng "WEBSITE · <host> · phiên đã lưu", có nút XÓA PHIÊN riêng; chọn nó rồi bấm MỞ OVERLAY là vào map không cần đăng nhập lại. Phiên được lưu bằng DPAPI (Windows) như vault IslePilot, không ghi ra log.
  - **Chế độ khách**: nút CHẾ ĐỘ KHÁCH · CHỈ XEM BẢN ĐỒ mở overlay chỉ có bản đồ + vị trí từ packet (Npcap), không cần tài khoản nào.
  - HUD gọn theo đúng phản hồi: **bỏ dòng chữ điều kiện** (MÁU/DIET/life stage) và trả panel chỉ số về bề rộng cũ; thay vào đó các **thanh màu được kéo khít tới con số %** (bỏ khoảng trống 8px, cột số thu còn 52px) — đúng chỗ cần "dài ra".
  - **Icon xương chỉ hiện ở server có trường fracture (SBTC Island)**, các server khác không có dòng này; xương luôn **màu trắng** (xương thì phải trắng) và luôn hiện **`XƯƠNG n%`** bên cạnh — số chuyển đỏ khi xương chưa lành. Icon trái tim (máu) đổi sang **màu đỏ**.
  - **Thanh xương** giờ cùng dạng với các thanh khác (icon + thanh + %), xương luôn trắng còn thanh chuyển đỏ khi nứt; giá trị nứt được giữ trong 12 giây nên mẫu 100% thoáng qua không làm thanh nhảy.
  - **Npcap không bắt được gói dù đang trong server**: app chỉ đọc được UDP **IPv4** và chỉ liệt kê cổng UDP IPv4, nên khi client nối server qua **IPv6** thì card mở được mà đếm 0 gói (`Đã mở card mạng · chưa nhận gói game`). Nay bộ lọc bắt cả hai họ, bộ đọc parse được IPv6 (kể cả frame có VLAN tag) và danh sách cổng lấy thêm bảng UDP IPv6. Dòng chẩn đoán cũng đếm số gói thấy trên card để biết ngay là sai card hay sai cổng.
  - **Npcap**: nút **BẬT LẠI DRIVER** hiện cả khi capture đang kẹt ở CONNECTING (trước đây chỉ hiện khi lỗi nên không có cách bật lại driver), và dòng chẩn đoán nói rõ đang tắc ở bước nào (chưa nhận gói / đã nhận N gói mà chưa khoá được toạ độ).
  - **Chẩn đoán Npcap trong F8**: thêm dòng cho biết nó đang tắc ở đâu — cổng UDP của tiến trình game, tình trạng driver npcap, và card mạng sẽ dùng. Kèm đó, Npcap nay khởi động cho **mọi** nguồn có live map (trước đây chỉ IslePilot nên nguồn website như SBTC không bật Npcap).
  - **Vùng bản đồ theo site**: nguồn SBTC Island giờ đọc chính dữ liệu bản đồ công khai của server (`assets/data/map_pois.json` và `assets/maps/mapconfig.json`) và vẽ đúng 4 nhóm mà web đang bật mặc định — **areas, waters, landmarks, sanctuaries** — kèm màu của site, thay vì bộ zones Gateway đóng gói sẵn. Zone có bán kính (`r`) vẽ thành vòng tròn, còn lại là marker chấm; mặc định tắt các nhóm nhiễu (animals, plants, KI-spawns…) nên bản đồ không bị rối. Dữ liệu cache 10 phút.
  - **Trả Npcap về đúng bản cũ đã chạy được**: bỏ toàn bộ thay đổi IPv6/đổi cách liệt kê cổng mà mình thêm ở bản trước (nghi là nguyên nhân 285 gói mà không khớp cổng), giữ nguyên cách bắt gói + giải mã như bản ổn định. Chỉ giữ lại **dòng đếm gói** trong F8 để chẩn đoán.
  - **Sửa gốc lỗi Npcap "thấy gói nhưng không khớp cổng game"**: card mạng của bạn (VPN/tunnel) được Npcap trả về dạng **raw IP** (link type 101) — frame bắt đầu bằng header IP chứ không có header Ethernet, mà app lại luôn đọc ethertype ở byte 12 nên mọi gói đều bị loại. App giờ thử lần lượt layout Ethernet → raw IP → loopback, nên bắt được cả hai kiểu card. Đã kiểm chứng trên capture sống 45 giây lấy từ chính máy bạn: khoá được vị trí, toạ độ mượt, yaw đúng.
  - App ghi lại vùng nó nhận được từ server ra  (mỗi phút một lần) để đối chiếu/hiệu chuẩn bộ polygon mà không cần DevTools hay token.
  - **Thanh chỉ số fit động**: cột số giờ tự co theo nội dung (`Auto`) thay cho bề rộng cứng 52px, nên thanh luôn dừng đúng chỗ con số bắt đầu — không đè lên số, cũng không chừa khoảng trống thừa, dù là "1018 / 10018" hay "100%".
  - **Nút lấy vùng từ IslePilot web** trong F8: mở trang IslePilot bằng WebView2 (dùng chung phiên đăng nhập), tự bắt mọi phản hồi JSON có sanctuary/patrol/migration và lưu vào `%LOCALAPPDATA%\IsleLiveMapData\islepilot-sniff\` để nhúng làm bộ polygon chuẩn.
  - **Vùng bản đồ cố định cho mọi server**: overlay luôn vẽ bộ polygon đóng gói trong app (Assets/GatewayZones.json, 80 vùng: 7 sanctuary · 12 migration · 61 patrol) thay vì chờ server gửi vùng. Server tắt overlay, server không có vùng, hay server khác hệ thống — tất cả đều hiện cùng một bộ vùng. File  (ghi mỗi phút) giờ ghi đúng bộ đang vẽ để đối chiếu.
  - **Vùng lấy thẳng từ IslePilot**: mở trang bản đồ IslePilot bằng chính phiên đăng nhập của bạn, trích lớp SVG vùng (polygon sanctuary/migration/patrol kèm nhãn), đổi sang toạ độ bản đồ của app rồi nhúng thành asset  (48 vùng: 5 sanctuary · 10 migration · 33 patrol, đúng màu IslePilot). App luôn vẽ bộ này cho mọi server, không phụ thuộc server gửi gì; bộ Gateway cũ vẫn là dự phòng nếu asset lỗi.
  - Vùng đóng gói giờ vẽ **kể cả khi chưa vào server** (trước đây app chờ biết tên server mới dựng lớp vùng, nên màn hình NO ACTIVE DINOSAUR bị trắng vùng).
  - **Sửa lỗi khiến vùng không bao giờ hiện**: còn một điều kiện cũ `if (!hasHostZones) return [];` chặn luôn cả bộ vùng đóng gói, nên mọi thay đổi trước đó bị vô hiệu. Nay chỉ chặn vùng *từ server* khi feed stale, còn bộ vùng cố định luôn được vẽ.
  - Bộ vùng IslePilot lấy lại bằng đúng phép biến đổi của trang (không còn lệch): 42 vùng — 5 sanctuary (xanh), 9 migration (cam), 28 patrol (tím) — đã render chồng lên bản đồ app để đối chiếu, khớp với bản đồ islepilot.eu.
  - Thêm nút kết nối **SBTC ISLAND** ở màn hình chọn server (cạnh EraGaming, PANDORA, SDVN #3).
  - Bỏ toàn bộ phần đọc chỉ số từ packet: không còn switch trong F8, không còn file chẩn đoán `pcap-vitals.txt`; Npcap vẫn giữ vai trò cũ là nguồn vị trí dự phòng khi server không có web.

- (local, chưa phát hành) Bỏ **Garage** và **Skin 3D** trong app, chuyển sang trang của server:
  - Hai tab Garage và Skin 3D (kèm trình xem 3D WebView2, lệnh cất/lấy dino, sửa màu skin) đã được gỡ khỏi cửa sổ Cẩm nang cùng toàn bộ phần nạp dữ liệu tương ứng.
  - Màn hình chọn server có thêm hai nút **VAULT** và **SKIN STUDIO**, mở thẳng `sbtcislandd.com/vault` và `sbtcislandd.com/studio` trong cửa sổ WebView2 dùng chung profile đăng nhập — nên vào là đã có phiên Steam sẵn, không phải đăng nhập lại.
  - Vault của site làm được việc garage từng làm (cất/lấy dino, đổi tên, chọn skin đã lưu, mutations) và Skin Studio chỉnh màu theo từng phần cơ thể; bảng màu mặc định của từng loài lấy từ `/api/palettes` (công khai, 22 loài, màu linear RGBA).

- Vệt đường đi (Path Trail) không còn bị xoá sạch sau 3 tiếng:
  - Trước đây cửa sổ 3 tiếng tính theo đồng hồ thực, nên chỉ cần mở app sau một giờ nghỉ là toàn bộ đường đi cũ biến mất — nhìn như "đúng 3 tiếng xoá hết một lần".
  - Nay cửa sổ cuốn theo chính vệt đường: chỉ những đoạn cũ hơn 3 tiếng tính từ điểm mới nhất mới bị bỏ, nên đường đi vẫn còn khi bạn quay lại, và chỉ ngắn dần khi bạn tiếp tục di chuyển. Vẫn xoá được bằng nút xoá trail, hoặc tự reset khi đổi server/chủng loài.

# Isle Live Map 1.8.6

- Sửa lỗi phiên IslePilot bị coi là hết hạn sau khi đăng nhập cho server dùng tên miền riêng:
  - Token overlay là phiên theo **tài khoản Steam**, không phải theo host. Bản 1.8.5 lưu mỗi host một vault riêng, nên đăng nhập cho SDVN #3 xoay token và mục IslePilot Network cũ bị server coi là hết hạn — app lại đòi đăng nhập Steam.
  - Nay chỉ còn **một vault dùng chung**; host chỉ quyết định gọi API ở đâu. Danh sách tài khoản vẫn hiện đủ hai mục `STEAM · ISLEPILOT` và `STEAM · SDVN #3` cho cùng tài khoản, chọn mục nào thì kết nối host đó nhưng dùng chung một phiên — không phải đăng nhập lại lần nữa.
  - Vault riêng theo host do 1.8.5 tạo được tự động gộp vào vault chung rồi xoá, nên tài khoản đã đăng nhập cho SDVN #3 vẫn dùng được ngay sau khi cập nhật.

# Isle Live Map 1.8.5

- Sửa lỗi hướng nhìn (heading) NPCAP luôn sai hoặc đứng yên một hướng:
  - Gói `ServerMove` gửi rotator (`pitch`/`yaw`/`roll`) **ngay sau** vector vị trí, mỗi trục là 1 bit "có giá trị" + 16 bit độ (`FRotator::SerializeCompressedShort`). Decoder cũ bỏ qua 16 bit trước khi đọc nên lấy nhầm sang vùng bit khác và cho ra `yaw = 0` hoặc giá trị ngẫu nhiên.
  - Decoder giờ đọc đúng rotator liền sau vị trí và loại bỏ các khung hình có `pitch`/`roll` bất khả thi, nên kim hướng quay đúng theo hướng người chơi đang nhìn.
- Sửa lỗi vị trí NPCAP hiển thị sai chỗ và không cập nhật:
  - Decoder cũ quét offset bit và khoá vào offset **đầu tiên** đủ 3 khung hình "hợp lệ". Vì khung hình đứng yên (timestamp game không đổi) vẫn được tính là hợp lệ, decoder khoá nhầm vào một vùng bit rác và báo vị trí gần giữa bản đồ, giữ nguyên như vậy suốt phiên.
  - Decoder giờ yêu cầu timestamp game trong gói phải tiến đúng nhịp với thời gian thực của phiên bắt gói, đồng thời kiểm tra vị trí nằm trong phạm vi bản đồ: chỉ layout thật của RPC di chuyển mới đủ điều kiện khoá.
  - Khi layout gói thay đổi hoặc tạm ngưng quá 3 giây, decoder tự mở khoá và dò lại thay vì giữ nguyên offset đã chết; toạ độ mốc từ clipboard cũng tự hết hạn sau 15 giây để hồi sinh/teleport không làm mất vị trí.
- Sửa lỗi **Copy Asset Location bị lệch/hiển thị sai chỗ**:
  - Lệnh copy trong game in giá trị Bắc–Nam (`Lat`) trước rồi mới tới Đông–Tây (`Long`), trong khi vị trí đọc từ gói tin lại đưa Đông–Tây lên trước; parser cũ dùng nguyên thứ tự nên hai nguồn lệch nhau và marker nhảy sang vị trí đối xứng khi bấm copy.
  - Đối chiếu một lần copy với vị trí decode được ngay lúc đó (đứng yên) cho thấy hai giá trị bị hoán vị, nên parser giờ đổi chỗ hai số đầu trước khi chiếu lên bản đồ.
  - Thống nhất quy ước trục cho mọi nguồn (NPCAP, clipboard, IslePilot/Pandora REST): trục X là Đông–Tây (trục ngang của ảnh), trục Y là Bắc–Nam. Sửa theo đó phép chiếu `GatewayMapProjection`, công thức heading di chuyển và `FromUnrealYaw = yaw + 90` (khớp với heading suy ra từ calibration của server, có test chéo).
- Chặn dữ liệu replication tổng hợp (chiều server → client) khi chưa có mốc Copy Asset: luồng này chứa chuyển động của mọi người xung quanh nên có thể hiển thị nhầm vị trí người chơi khác.
- Làm mượt chuyển động và hướng nhìn, bỏ kiểu chạy–dừng theo nhịp gói:
  - Bản đồ overlay trước đây khởi động lại animation pan 110–360 ms cho từng gói vị trí (~5 gói/giây), nên bản đồ cứ chạy–dừng theo nhịp gói. Nay bản đồ bám mục tiêu liên tục theo từng khung hình.
  - Kim hướng và bản đồ (khi bật chế độ xoay) cũng khởi động lại animation 70–220 ms cho từng gói, nên lúc quay camera kim bị giật từng nhịp. Nay cả hai bám mục tiêu theo từng khung hình, đi đường ngắn nhất, và đổi chế độ xoay cũng chuyển mượt.
  - Không dựng lại layer marker của người chơi khác mỗi gói vị trí (vị trí marker chỉ phụ thuộc kích thước bản đồ).
- Danh sách tài khoản Steam ở Home gộp chung mọi nguồn IslePilot: tài khoản đăng nhập cho server chạy host riêng (ví dụ SDVN #3) được lưu và hiện ngay trong danh sách với nhãn riêng `STEAM · SDVN #3`, chọn tài khoản nào thì nút MỞ OVERLAY kết nối đúng host của tài khoản đó (không cần bấm lại nút server mỗi lần).
- Hỗ trợ server **[SEA/VN]-SDVN-#3-X3** (`3.sdvn.org`): server này chạy một bản IslePilot trên tên miền riêng nên có nút kết nối riêng ở Home. App đăng nhập Steam một lần cho host đó, rồi dùng thẳng overlay API của host (chỉ số dino, marker, vùng bản đồ) và lưu token riêng theo host — phiên IslePilot Network không bị ảnh hưởng.
  - Toàn bộ endpoint overlay (me/map/garage/skin + WebSocket `/ows`) giờ lấy theo host của nguồn thay vì cố định `islepilot.eu`.
  - Trang đăng nhập Steam chỉ cho phép điều hướng trong host của server đó và Steam.
- Bản đồ sạch khi server không gửi dữ liệu vùng: các mốc/vùng màu tím và vàng chỉ hiện khi feed bản đồ của server thực sự trả về polygon vùng. Khi chỉ dùng NPCAP để xem vị trí (hoặc nguồn chỉ có marker), bản đồ chỉ còn ảnh bản đồ + marker của bạn kèm vệt đường đi, không còn hàng chục mốc vùng chồng lên nhau gây rối. Bộ zone Gateway đóng gói chỉ còn là phương án dự phòng cho lúc feed đang chạy mà thiếu polygon.
- Giữ chỉ điểm người chơi luôn rõ: trước đây khi phiên telemetry (IslePilot) bị stale/reconnecting thì cả marker và kim hướng bị giảm opacity còn 70% nên nhìn bị mờ, dù vị trí đến từ nguồn NPCAP độc lập. Nay chỉ các chỉ số (máu/thể lực/đói/nước) mờ theo phiên telemetry, còn marker và kim hướng giữ nguyên độ rõ.

# Isle Live Map 1.8.4

- Tự động xóa và reset đường đi cũ (Path Trail) khi người chơi chuyển sang server khác hoặc đổi chủng loài khủng long (Dino).
- Sửa lỗi lưu skin draft: trước khi lưu, hub tự động lấy danh sách draft hiện có để tránh bị server đè mất draft cũ.

# Isle Live Map 1.8.3

- Sửa triệt để lỗi "The JSON value could not be converted to System.Int32" ở trường `glitchLab.layers.<layer>.x`:
  - Hỗ trợ đầy đủ các giá trị số thực dạng dấu phẩy động (float/double) cho các kênh màu và tham số Glitch Lab (`x`, `y`, `z`, `a`, `pi`, `sv`).
  - Thêm `SafeGlitchLabConverter` bảo vệ an toàn, không bao giờ ném ngoại lệ làm hỏng tiến trình nạp danh sách draft.
  - Bổ sung cơ chế tự phục hồi `ResilientSkinDraftListConverter`: nếu một draft bất kỳ bị hỏng cấu trúc dữ liệu từ server, client sẽ tự động bỏ qua riêng draft đó và tiếp tục tải bình thường toàn bộ các draft hợp lệ khác.
  - Thêm các bộ chuyển đổi linh hoạt (Flexible Converters) cho `int`, `double` và `DateTimeOffset`: tự động chuyển đổi an toàn giữa số nguyên, số thực, chuỗi số, dấu thời gian Unix và null.

# Isle Live Map 1.8.2

- Khắc phục lỗi crash ứng dụng (crash map) khi chuyển vào tab Skin 3D trên một số máy:
  - Bọc an toàn toàn bộ quá trình khởi tạo WebView2 và DirectComposition trong `try-catch`; hiển thị thông báo thay vì đóng ứng dụng khi thiết bị không hỗ trợ DirectComposition hoặc thiếu WebView2 Runtime.
  - Xử lý bất đồng bộ sự kiện `ProcessFailed` khi GPU process của WebView2 gặp sự cố, tránh lỗi COM re-entrant gây sập tiến trình.
  - Thêm cờ `--disable-gpu-process-crash-limit --enable-webgl` tăng độ ổn định của tiến trình đồ họa 3D.
  - Thiết lập `e.Handled = true` trên DispatcherUnhandledException để bảo vệ overlay map không bị thoát đột ngột khi xảy ra lỗi giao diện.
- Sửa lỗi tính năng Show All Skin Drafts không hiển thị draft:
  - Hỗ trợ giải mã màu sắc dạng mảng số thực Linear RGB (`[r, g, b, 1]`) từ web IslePilot sang sRGB hex chuẩn (`#RRGGBB`).
  - Đọc chính xác loài khủng long từ trường `class` (`BP_Tyrannosaurus_C`) trong payload draft của IslePilot.
  - Cho phép tải và xem danh sách toàn bộ skin draft ngay cả khi chưa vào game hoặc chưa có khủng long hiện tại.
  - Hiển thị tên loài đi kèm khi bật chế độ "SHOW ALL DRAFT" (ví dụ: `trexcano (Tyrannosaurus)`).
  - Bắt lỗi xác thực IslePilot (`IslePilotOverlayAuthenticationException`) an toàn khi mở tab Skin 3D, hiển thị thông báo "CHƯA ĐĂNG NHẬP ISLEPILOT HOẶC PHIÊN ĐÃ HẾT HẠN" thay vì gây lỗi unhandled.

# Isle Live Map 1.8.1

- Sửa lỗi áp dụng bảng màu Skin: chuyển đổi chính xác chuẩn Linear RGB (IEC 61966-2-1), loại bỏ hiện tượng bạc màu/nhạt màu và khớp 100% độ đậm và chi tiết như web IslePilot.
- Chuẩn hóa tên Blueprint Class của khủng long theo chuẩn PascalCase của Unreal Engine (ví dụ: `BP_Tyrannosaurus_C`), khắc phục lỗi server The Isle không nhận màu khi gửi từ Hub.
- Đồng bộ cấu trúc payload gửi màu với web: chỉ gửi các trường hợp lệ, loại bỏ các trường thừa (`display`, `female_display`).
- Sửa lỗi telemetry định kỳ ghi đè giới tính và thông số biến thể khi nạp Skin Draft.
- Cải tiến danh sách Skin Drafts: bấm nút áp dụng ở draft sẽ nạp mã màu và thông số vào form xem trước 3D mà không gửi vội tới server; chỉ gửi tới game khi người dùng bấm nút Áp dụng chính.
- Tự động nhận diện server ID cho các máy chủ SBTC Island.

# Isle Live Map 1.8.0

- Tạm ẩn hoàn toàn SBTC Voice Chat trong F8 cho tới khi trải nghiệm overlay đạt yêu cầu; phần runtime được giữ lại nhưng không thể tự bật theo server.
- Sửa màu Garage/Skin 3D bằng texture pattern và mask thật của từng loài: tách đúng thân, hoa văn, mạn sườn, bụng, chi tiết, display, mắt, răng, miệng và móng thay vì phủ một màu lên toàn model.
- Cache và kiểm tra chữ ký PNG/WebP cho asset màu 3D, tránh lưu nhầm trang lỗi Cloudflare/CDN thành texture.
- Sửa Npcap mắc ở `CONNECTING`: chủ động phát hiện driver chưa chạy, nút `MỞ LẠI` khởi động driver qua UAC và capture đúng cả hai chiều UDP trên cổng game.
- Sửa Npcap dùng nhầm calibration Gateway cũ khiến vị trí West Rail bị chiếu sang Highlands; tọa độ và hướng camera nay dùng calibration động từ IslePilot map.
- Cập nhật decoder cho packet-handler thay đổi theo từng phiên của SBTC; tọa độ và hướng camera đạt khoảng 5 mẫu/giây trong phép đo dài, không còn swap X/Y khiến North Jungle bị đặt sang Mud Flats.
- Sửa Garage/Skin 3D không tải được model khi CDN chậm: thêm browser headers, timeout hai phút, ba lần retry, cache nguyên tử và kiểm tra đầy đủ GLB v2; cache hỏng tự bị xóa để tải lại.
- Sửa Garage/Skin 3D khởi tạo WebView khi chưa gắn vào cửa sổ và dùng chung WebView environment ổn định.
- Khi Npcap live, các nguồn REST/clipboard cũ không còn giành quyền khiến marker nhảy sai vị trí.
- Làm mới Garage/Skin 3D, nền waterfall local, cài đặt F8, zoom map và thêm nút bật/tắt tự ẩn overlay khi Alt+Tab; Copy Asset luôn bật và Npcap tự fallback về WS/REST khi không khả dụng.
- Sửa so sánh phiên bản update và thay phiên app cũ khi mở phiên mới, đồng thời tiếp tục giữ dữ liệu đăng nhập trong vùng dữ liệu người dùng.

# Isle Live Map 1.7.16

- Khôi phục đúng vault v3 của các bản 1.7.10–1.7.12 từ thư mục cài đặt cũ; trước đây migration chỉ thử khóa v1/v2 nên một số máy hiển thị mất toàn bộ tài khoản sau update.
- Không còn tự xóa cookie và site data khi cửa sổ đăng nhập mở; chỉ xóa phiên WebView khi người dùng chủ động bấm `ĐỔI TÀI KHOẢN`.
- Phân loại 403, 429, 500, 502, 503, 504, timeout và HTML challenge khi IslePilot/CDN quá tải là lỗi tạm thời; giữ vault và tiếp tục polling để tự phục hồi.

# Isle Live Map 1.7.15

- Không còn tự xóa tài khoản Steam đã lưu khi một endpoint IslePilot hoặc WebSocket tạm trả 401/403; credential chỉ bị xóa khi người dùng bấm `XÓA TÀI KHOẢN`.
- Chỉ `/me` mới kết luận phiên hết hạn sau ba lần xác minh liên tiếp; lỗi quyền của `/map`, `/markers` và WS giữ REST session cùng dữ liệu tài khoản hoạt động.
- Xóa đầy đủ cookie và site data trong WebView2 khi mở luồng đăng nhập để việc đổi tài khoản Steam không bị dính phiên cũ.

# Isle Live Map 1.7.14

- Đặt `ROTATE` mặc định là OFF cho lần chạy mới và khi file layout không hợp lệ; lựa chọn đã lưu của người dùng cũ vẫn được giữ nguyên.

# Isle Live Map 1.7.13

- Sửa mất phiên đăng nhập do UserData từng trùng với install root của Velopack: dữ liệu runtime nay nằm riêng tại `%LocalAppData%\IsleLiveMapData`.
- Ưu tiên phục hồi credential/layout/WebView2 từ install root cho máy chưa bị dọn, nhưng không bao giờ xóa hoặc sửa cấu trúc cài đặt Velopack.
- Giữ migration từ các vùng dữ liệu cũ; chỉ xóa nguồn cũ sau khi credential được đọc an toàn.

# Isle Live Map 1.7.12

- Single-flight toàn bộ bước check update: mọi lời gọi đồng thời/lặp trong cùng phiên app dùng chung đúng một request, không tạo request chồng.
- Chỉ check một lần khi Home khởi động, không polling nền; feed trực tiếp có timeout 15 giây và package chỉ tải sau khi người dùng xác nhận.
- Đổi trạng thái lỗi thành `KHÔNG CHECK ĐƯỢC UPDATE` để không gây hiểu nhầm app hoặc telemetry đang offline.

# Isle Live Map 1.7.11

- Sửa `UPDATE OFFLINE` do GitHub REST API giới hạn request theo IP: updater nay đọc feed/package trực tiếp từ GitHub Release, không dùng API và không cần nhúng access token.
- Giữ nguyên cơ chế xác nhận trước khi tải/cài; lỗi kiểm tra update vẫn không ảnh hưởng đăng nhập và overlay.

# Isle Live Map 1.7.10

- Chuyển toàn bộ dữ liệu người dùng sang `%LocalAppData%\IsleLiveMap`, không dùng tên cá nhân trong đường dẫn, mutex hoặc DPAPI namespace.
- Tự nhập session từ cả hai vùng dữ liệu cũ, ưu tiên dữ liệu mới hơn và xóa đúng thư mục app cũ sau khi migration thành công.

# Isle Live Map 1.7.9

- Giữ nguyên đăng nhập khi nâng cấp: tự chuyển token IslePilot đã mã hóa, WebView2 cookies và thiết lập overlay từ vùng dữ liệu của các bản cũ sang vùng dữ liệu hiện tại.
- Migration chỉ chạy khi dữ liệu đích chưa có, không ghi đè phiên mới và không chặn app khởi động nếu dữ liệu cũ hỏng hoặc bị khóa.

# Isle Live Map 1.7.8

- Hiện dialog custom ngay khi phát hiện update, với lựa chọn cập nhật ngay hoặc bỏ qua.
- Khi bỏ qua, footer hiển thị `CẦN UPDATE` cùng nút `UPDATE` để người dùng chủ động cập nhật sau.
- Tách kiểm tra khỏi tải package: app không tải update cho tới khi người dùng xác nhận; tải lỗi vẫn giữ nút retry.

## 1.7.7

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
