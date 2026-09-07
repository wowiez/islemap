namespace TheIsleOverlay.App;

public enum MutationTrack
{
    Survival,
    Combat,
    Movement
}

public sealed record MutationRecommendation(
    int Priority,
    MutationTrack Track,
    string Name,
    string VietnameseName,
    string Effect,
    string Why,
    string Tag)
{
    public string DisplayName => $"{Name} ({VietnameseName})";
}

public sealed record SpeciesMutationGuide(
    string Name,
    string Diet,
    string Role,
    string Summary,
    IReadOnlyList<MutationRecommendation> Recommendations);

public static class MutationGuideCatalog
{
    public static IReadOnlyList<SpeciesMutationGuide> Species { get; } =
    [
        Carnivore("Tyrannosaurus", "Apex · đấu trực diện", "Ưu tiên chống burst, hồi phục và duy trì tài nguyên trong những trận đánh dài."),
        Carnivore("Allosaurus", "Đấu sĩ bleed cơ động", "Cân bằng sức bền với khả năng bám mục tiêu đang chảy máu."),
        Runner("Carnotaurus", "Săn đuổi · charge", "Giữ stamina và tài nguyên để liên tục tạo góc charge an toàn."),
        Carnivore("Ceratosaurus", "Brawler · ăn xác", "Tăng độ lì khi tranh xác và giao chiến cự ly gần."),
        Aquatic("Baryonyx", "Ven sông · phục kích", "Kết hợp lợi thế dưới nước với khả năng sống sót khi lên bờ."),
        NightHunter("Dilophosaurus", "Săn đêm · venom", "Kéo dài lợi thế ban đêm và giữ nhịp bám đuổi sau khi áp venom."),
        Climber("Herrerasaurus", "Leo trèo · ambush", "Giảm rủi ro khi rơi, tiết kiệm stamina và hồi phục sau cú lao."),
        Runner("Austroraptor", "Săn cá · đánh nhanh", "Ưu tiên stamina, nước và thoát giao tranh thay vì đấu đổi sát thương."),
        NightHunter("Troodon", "Pack hunter · venom", "Tập trung sống sót, stamina và kết liễu mục tiêu đã yếu."),
        Runner("Omniraptor", "Pounce · pack hunter", "Giữ stamina cho pounce, giảm bleed và duy trì truy đuổi."),
        Aquatic("Deinosuchus", "Aquatic apex · phục kích", "Tối đa lợi thế dưới nước và khả năng chịu đòn khi giữ con mồi."),
        Flyer("Pteranodon", "Bay · trinh sát", "Ưu tiên stamina cất cánh, nước và an toàn khi hạ cánh."),
        HerbivoreTank("Triceratops", "Tank · phản công", "Giữ máu, chống bleed và chuyển sát thương thành stamina trong giao tranh."),
        HerbivoreTank("Stegosaurus", "Tank · kiểm soát đuôi", "Tăng độ lì và khả năng duy trì vị trí trong combat kéo dài."),
        HerbivoreTank("Diabloceratops", "Brawler cơ động", "Cân bằng chống chịu với stamina để chủ động vào/ra giao tranh."),
        HerbivoreTank("Kentrosaurus", "Phòng thủ · phản sát thương", "Giữ trạng thái phòng thủ lâu hơn và hạn chế bleed khi bị vây."),
        HerbivoreRunner("Tenontosaurus", "Đấu sĩ cơ động", "Stamina và chống bleed giúp duy trì chuỗi kick/tail slam."),
        HerbivoreRunner("Maiasaura", "Herd · speed tank", "Tối ưu di chuyển theo đàn, cảnh giới và khả năng sống lâu ngoài vùng thức ăn."),
        HerbivoreTank("Pachycephalosaurus", "Fracture · hit-and-run", "Giữ stamina cho ram và giảm rủi ro fracture khi đổi đòn."),
        SmallHerbivore("Dryosaurus", "Né tránh · sinh tồn", "Ẩn dấu vết, tiết kiệm tài nguyên và luôn có stamina để chạy."),
        SmallHerbivore("Hypsilophodon", "Nhỏ · đánh lạc hướng", "Tập trung thoát thân, stamina và giảm khả năng bị truy dấu."),
        Aquatic("Beipiaosaurus", "Bán thủy sinh · omnivore", "Tận dụng tốc độ nước nhưng vẫn đủ tài nguyên để hoạt động trên bờ."),
        Runner("Gallimimus", "Flock · tốc độ", "Giữ stamina, nước và hiệu quả di chuyển đường dài theo đàn.", "Ăn tạp")
    ];

    private static SpeciesMutationGuide Carnivore(string name, string role, string summary) => Guide(
        name, "Ăn thịt", role, summary,
        SurvivalCarnivore(), CombatCarnivore(), MovementRunner());

    private static SpeciesMutationGuide Runner(string name, string role, string summary, string diet = "Ăn thịt") => Guide(
        name, diet, role, summary,
        SurvivalRunner(), CombatHunter(), MovementRunner());

    private static SpeciesMutationGuide NightHunter(string name, string role, string summary) => Guide(
        name, "Ăn thịt", role, summary,
        SurvivalCarnivore(),
        [
            Pick(1, MutationTrack.Combat, "Hemomania", "Cuồng huyết", "+5% sát thương lên mục tiêu chảy máu", "Khuếch đại áp lực sau cú mở giao tranh.", "CARNIVORE"),
            Pick(2, MutationTrack.Combat, "Accelerated Prey Drive", "Bản năng kết liễu", "+10% sát thương khi mục tiêu dưới 35% máu", "Giúp pack kết thúc con mồi trước khi nó thoát.", "CARNIVORE"),
            Pick(3, MutationTrack.Combat, "Congenital Hypoalgesia", "Giảm đau bẩm sinh", "Giảm 15% sát thương từ loài lớn hơn", "Tạo khoảng an toàn khi phải áp sát mục tiêu lớn.", "DEFENSE")
        ],
        [
            Pick(1, MutationTrack.Movement, "Nocturnal", "Thích nghi ban đêm", "Hồi máu/locked-health nhanh hơn vào ban đêm", "Phù hợp nhịp săn và hồi phục ban đêm.", "NIGHT"),
            Pick(2, MutationTrack.Movement, "Augmented Tapetum", "Tăng cường thị giác đêm", "Tăng tầm nhìn ban đêm", "Đọc hướng con mồi và địa hình rõ hơn trong bóng tối.", "UNLOCK · SLOT 2"),
            Pick(3, MutationTrack.Movement, "Multichambered Lungs", "Phổi đa ngăn", "Tăng ngưỡng hồi stamina", "Giảm thời gian phải ngắt nhịp truy đuổi.", "UNLOCK")
        ]);

    private static SpeciesMutationGuide Aquatic(string name, string role, string summary) => Guide(
        name, name == "Beipiaosaurus" ? "Ăn tạp" : "Ăn thịt", role, summary,
        [
            Pick(1, MutationTrack.Survival, "Sustained Hydration", "Duy trì nước", "Nước giảm chậm hơn 20%", "Tăng thời gian hoạt động khi phải rời nguồn nước an toàn.", "SURVIVAL"),
            Pick(2, MutationTrack.Survival, "Efficient Digestion", "Tiêu hóa hiệu quả", "Thức ăn giảm chậm hơn 20%", "Giảm số lần phải mạo hiểm đi tìm thức ăn.", "SURVIVAL"),
            Pick(3, MutationTrack.Survival, "Cellular Regeneration", "Tái tạo tế bào", "Hồi máu nhanh hơn 15%", "Hồi lại sau phục kích hoặc tranh chấp lãnh thổ.", "RECOVERY")
        ],
        CombatCarnivore(),
        [
            Pick(1, MutationTrack.Movement, "Hydrodynamic", "Thủy động lực", "+15% tốc độ bơi", "Tăng khả năng tiếp cận hoặc thoát khỏi mục tiêu dưới nước.", "AQUATIC"),
            Pick(2, MutationTrack.Movement, "Increased Inspiratory Capacity", "Tăng dung tích hô hấp", "+15% dung lượng O₂", "Cho phép lặn và chờ phục kích lâu hơn.", "AQUATIC"),
            Pick(3, MutationTrack.Movement, "Submerged Optical Retention", "Thị giác dưới nước", "+5% tầm nhìn dưới nước", "Giữ dấu mục tiêu tốt hơn ở vùng nước đục.", "AQUATIC")
        ]);

    private static SpeciesMutationGuide Flyer(string name, string role, string summary) => Guide(
        name, "Ăn thịt", role, summary,
        SurvivalRunner(), CombatHunter(),
        [
            Pick(1, MutationTrack.Movement, "Reinforced Tendons", "Gân cường hóa", "Giảm stamina khi nhảy/cất cánh", "Lựa chọn ưu tiên cho những lần cất cánh liên tục.", "UNLOCK"),
            Pick(2, MutationTrack.Movement, "Multichambered Lungs", "Phổi đa ngăn", "Tăng ngưỡng hồi stamina", "Giữ thời gian bay và phục hồi ổn định hơn.", "UNLOCK"),
            Pick(3, MutationTrack.Movement, "Increased Inspiratory Capacity", "Tăng dung tích hô hấp", "+15% dung lượng O₂", "Hữu ích khi câu cá hoặc thoát xuống nước.", "MOVEMENT")
        ]);

    private static SpeciesMutationGuide Climber(string name, string role, string summary) => Guide(
        name, "Ăn thịt", role, summary,
        SurvivalRunner(), CombatHunter(),
        [
            Pick(1, MutationTrack.Movement, "Enlarged Meniscus", "Sụn chêm mở rộng", "Fall damage trừ stamina trước máu", "Giảm hậu quả khi hụt cú lao hoặc rơi khỏi cây.", "MOVEMENT"),
            Pick(2, MutationTrack.Movement, "Reinforced Tendons", "Gân cường hóa", "Giảm stamina khi nhảy", "Cho phép đổi vị trí và leo trèo thường xuyên hơn.", "UNLOCK"),
            Pick(3, MutationTrack.Movement, "Multichambered Lungs", "Phổi đa ngăn", "Tăng ngưỡng hồi stamina", "Rút ngắn khoảng nghỉ giữa các pha phục kích.", "UNLOCK")
        ]);

    private static SpeciesMutationGuide HerbivoreTank(string name, string role, string summary) => Guide(
        name, "Ăn cỏ", role, summary,
        SurvivalHerbivore(), CombatHerbivore(), MovementHerbivore());

    private static SpeciesMutationGuide HerbivoreRunner(string name, string role, string summary) => Guide(
        name, "Ăn cỏ", role, summary,
        SurvivalHerbivore(), CombatHerbivore(), MovementRunner());

    private static SpeciesMutationGuide SmallHerbivore(string name, string role, string summary) => Guide(
        name, "Ăn cỏ", role, summary,
        [
            Pick(1, MutationTrack.Survival, "Featherweight", "Dấu chân nhẹ", "Dấu chân biến mất nhanh hơn 50%", "Giảm khả năng bị thợ săn bám dấu.", "STEALTH"),
            Pick(2, MutationTrack.Survival, "Efficient Digestion", "Tiêu hóa hiệu quả", "Thức ăn giảm chậm hơn 20%", "Ít phải dừng lại ở khu vực nguy hiểm.", "SURVIVAL"),
            Pick(3, MutationTrack.Survival, "Sustained Hydration", "Duy trì nước", "Nước giảm chậm hơn 20%", "Mở rộng khoảng cách giữa các lần ghé nguồn nước.", "SURVIVAL")
        ],
        CombatHerbivore(), MovementRunner());

    private static IReadOnlyList<MutationRecommendation> SurvivalCarnivore() =>
    [
        Pick(1, MutationTrack.Survival, "Efficient Digestion", "Tiêu hóa hiệu quả", "Thức ăn giảm chậm hơn 20%", "Giảm thời gian lộ vị trí khi phải săn hoặc ăn xác.", "SURVIVAL"),
        Pick(2, MutationTrack.Survival, "Cellular Regeneration", "Tái tạo tế bào", "Hồi máu nhanh hơn 15%", "Quay lại giao tranh nhanh hơn sau khi rút lui.", "RECOVERY"),
        Pick(3, MutationTrack.Survival, "Sustained Hydration", "Duy trì nước", "Nước giảm chậm hơn 20%", "Cho phép săn xa nguồn nước lâu hơn.", "SURVIVAL")
    ];

    private static IReadOnlyList<MutationRecommendation> SurvivalRunner() =>
    [
        Pick(1, MutationTrack.Survival, "Efficient Digestion", "Tiêu hóa hiệu quả", "Thức ăn giảm chậm hơn 20%", "Duy trì hành trình dài mà không phải dừng kiếm ăn liên tục.", "SURVIVAL"),
        Pick(2, MutationTrack.Survival, "Sustained Hydration", "Duy trì nước", "Nước giảm chậm hơn 20%", "Giảm lệ thuộc vào các điểm nước đông người.", "SURVIVAL"),
        Pick(3, MutationTrack.Survival, "Featherweight", "Dấu chân nhẹ", "Dấu chân biến mất nhanh hơn 50%", "Cắt dấu tốt hơn sau khi thoát khỏi giao tranh.", "STEALTH")
    ];

    private static IReadOnlyList<MutationRecommendation> SurvivalHerbivore() =>
    [
        Pick(1, MutationTrack.Survival, "Cellular Regeneration", "Tái tạo tế bào", "Hồi máu nhanh hơn 15%", "Tăng khả năng phục hồi giữa các đợt bị săn.", "RECOVERY"),
        Pick(2, MutationTrack.Survival, "Epidermal Fibrosis", "Xơ hóa biểu bì", "+15% kháng bleed", "Rất hữu ích khi bị nhiều carnivore cấu rỉa.", "BLEED"),
        Pick(3, MutationTrack.Survival, "Xerocole Adaptation", "Thích nghi khô hạn", "Ăn cây giúp hồi một phần nước", "Giảm nhu cầu rời đàn để tìm nước.", "HERBIVORE")
    ];

    private static IReadOnlyList<MutationRecommendation> CombatCarnivore() =>
    [
        Pick(1, MutationTrack.Combat, "Congenital Hypoalgesia", "Giảm đau bẩm sinh", "Giảm 15% sát thương từ loài lớn hơn", "Tăng biên an toàn khi đổi đòn với mục tiêu nặng hơn.", "DEFENSE"),
        Pick(2, MutationTrack.Combat, "Hemomania", "Cuồng huyết", "+5% sát thương lên mục tiêu chảy máu", "Tăng hiệu quả khi cả nhóm duy trì bleed.", "CARNIVORE"),
        Pick(3, MutationTrack.Combat, "Accelerated Prey Drive", "Bản năng kết liễu", "+10% sát thương khi mục tiêu dưới 35% máu", "Rút ngắn giai đoạn nguy hiểm cuối trận.", "CARNIVORE")
    ];

    private static IReadOnlyList<MutationRecommendation> CombatHunter() =>
    [
        Pick(1, MutationTrack.Combat, "Accelerated Prey Drive", "Bản năng kết liễu", "+10% sát thương khi mục tiêu dưới 35% máu", "Phù hợp lối đánh bám đuổi và kết thúc nhanh.", "CARNIVORE"),
        Pick(2, MutationTrack.Combat, "Epidermal Fibrosis", "Xơ hóa biểu bì", "+15% kháng bleed", "Giảm áp lực khi phải rút khỏi một pha pounce lỗi.", "BLEED"),
        Pick(3, MutationTrack.Combat, "Congenital Hypoalgesia", "Giảm đau bẩm sinh", "Giảm 15% sát thương từ loài lớn hơn", "Hạn chế bị one-shot khi săn mục tiêu lớn.", "DEFENSE")
    ];

    private static IReadOnlyList<MutationRecommendation> CombatHerbivore() =>
    [
        Pick(1, MutationTrack.Combat, "Tactile Endurance", "Sức bền xúc giác", "Chuyển sát thương nhận vào thành stamina", "Giữ tài nguyên để phản công hoặc rút lui.", "HERBIVORE · SLOT 2"),
        Pick(2, MutationTrack.Combat, "Truculency", "Phản kháng", "Tăng khả năng hất kẻ đang bám", "Giảm thời gian carnivore được giữ pounce/grapple.", "HERBIVORE"),
        Pick(3, MutationTrack.Combat, "Osteosclerosis", "Xơ cứng xương", "+20% kháng sát thương fracture", "Giảm rủi ro mất khả năng di chuyển sau fracture.", "DEFENSE")
    ];

    private static IReadOnlyList<MutationRecommendation> MovementRunner() =>
    [
        Pick(1, MutationTrack.Movement, "Multichambered Lungs", "Phổi đa ngăn", "Tăng ngưỡng hồi stamina", "Giữ tốc độ hành trình và giảm thời gian phải nghỉ.", "UNLOCK"),
        Pick(2, MutationTrack.Movement, "Wader", "Lội nước", "Giảm 25% cản trở ở vùng nước nông", "Hạn chế mất đà khi băng qua sông và đầm lầy.", "MOVEMENT"),
        Pick(3, MutationTrack.Movement, "Reinforced Tendons", "Gân cường hóa", "Giảm stamina khi nhảy", "Hữu ích cho loài dùng nhảy/pounce để đổi vị trí.", "UNLOCK")
    ];

    private static IReadOnlyList<MutationRecommendation> MovementHerbivore() =>
    [
        Pick(1, MutationTrack.Movement, "Photosynthetic Regeneration", "Hồi stamina quang hợp", "+10% hồi stamina ban ngày", "Kéo dài khả năng bảo vệ đàn trong thời gian hoạt động chính.", "HERBIVORE"),
        Pick(2, MutationTrack.Movement, "Multichambered Lungs", "Phổi đa ngăn", "Tăng ngưỡng hồi stamina", "Hỗ trợ giữ đội hình và rút khỏi vùng nguy hiểm.", "UNLOCK"),
        Pick(3, MutationTrack.Movement, "Wader", "Lội nước", "Giảm 25% cản trở ở vùng nước nông", "Tránh bị mất tốc độ khi di chuyển qua vùng ẩm thấp.", "MOVEMENT")
    ];

    private static SpeciesMutationGuide Guide(
        string name,
        string diet,
        string role,
        string summary,
        params IReadOnlyList<MutationRecommendation>[] groups) =>
        new(name, diet, role, summary, groups.SelectMany(group => group).ToArray());

    private static MutationRecommendation Pick(
        int priority,
        MutationTrack track,
        string name,
        string vietnameseName,
        string effect,
        string why,
        string tag) => new(priority, track, name, vietnameseName, effect, why, tag);
}
