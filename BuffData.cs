namespace SherlockMacro;

/// <summary>
/// ย้ายตาราง buff ID ต่อ class และชื่อ buff ทั้งหมดจากต้นฉบับ AHK มาตรงๆ
/// (เป็นข้อมูลล้วนๆ ไม่มี logic จึงพอร์ตแบบ 1:1 ได้เลย)
/// </summary>
public static class BuffData
{
    // object[] เพราะบางรายการในต้นฉบับ AHK ผสม string ปนกับตัวเลข (เช่น "1347", "FullProtection")
    public static readonly object[] Archer = { 76, 77, 78, 3, 72, 115, 116, 352, 429, 715, 722, 1252, 1256, 1261, 1262, 1263 };
    public static readonly object[] Acolyte = { 9, 15, 20, 21, 86, 410, 425, 426, 427, 1160, 1161, 1162, 1201, 716 };
    public static readonly object[] Swordman = { 1, 2, 103, 104, 105, 107, 316, 319, 320, 322, 1154, 1172, 1178 };
    public static readonly object[] Swordman2 = { 58, 59, 62, 68, 197, 391, 400, 402, 407, 1202, 1203, 1204, 1217, 1220, 1316 };
    public static readonly object[] Merchant = { 23, 24, 25, 26, 30, 117, 118, 188, 361, 461, 1248, 1249 };
    public static readonly object[] Mage = { 65, 31, 127, 186, 113, 198, 355, 717, 1152 };
    public static readonly object[] Thief = { 7, 114, 334, 337, 120, 1192, 1193, 1194, 1226, 1244, 181, 1245, 1243 };
    public static readonly object[] Gunslinger = { 758, 759, 1345, "1347", 1348, 1349, 1350, 1351, 1352, 1353, 1354 };
    public static readonly object[] Taekwondo = { 93, 92, 91, 90, 148, 146, 17, 1039, 1392, 143 };
    public static readonly object[] Summoner = { 920, 1367, 1368, 1370, 1371, 1372, 1373, 1374, 1375, 1377 };
    public static readonly object[] SuperNovice = { 3, 9, 15, 20, 21, 58, 1383, 1384 };
    public static readonly object[] Ninja = { 206, 208, 652 };
    public static readonly object[] Soulink = { 1365 };

    public static readonly object[] MoveToItem = { "252", "BubbleGumHE", "250", "312", "1169" };

    // เทียบเท่า ItemClass ในต้นฉบับ + การ Push(MoveToItem) เข้าไปตอนท้าย
    public static readonly object[] ItemClass = BuildItemClass();

    public static readonly object[] Item2Class =
    {
        904, 908, 909, 910, 911, 18, 1065, 0, 3, 271, 272, 273, 274, 275, 276, 867, 685, 664, 19, 1171, 1170
    };

    public static readonly string[] DebuffIDs = { "panacea", "622" };

    public static readonly string[] ClassList =
    {
        "Archer", "Acolyte", "Swordman", "Swordman2", "Merchant", "Mage", "Thief", "Gunslinger",
        "Taekwondo", "Summoner", "Ninja", "SuperNovice", "Soulink", "Item", "Item2"
    };

    private static object[] BuildItemClass()
    {
        object[] baseList =
        {
            10, 12, 37, 38, 39, 295, 491, 492, 493, 494, 495, 496, 19, "FullProtection", 898, 899, 900, 901
        };
        return baseList.Concat(MoveToItem).ToArray();
    }

    /// <summary>คืนรายการ ID ตาม class name — เทียบเท่า %name% (variable dereference) ในต้นฉบับ AHK</summary>
    public static object[] GetListForClass(string className) => className switch
    {
        "Archer" => Archer,
        "Acolyte" => Acolyte,
        "Swordman" => Swordman,
        "Swordman2" => Swordman2,
        "Merchant" => Merchant,
        "Mage" => Mage,
        "Thief" => Thief,
        "Gunslinger" => Gunslinger,
        "Taekwondo" => Taekwondo,
        "Summoner" => Summoner,
        "Ninja" => Ninja,
        "SuperNovice" => SuperNovice,
        "Soulink" => Soulink,
        "Item" => ItemClass,
        "Item2" => Item2Class,
        _ => Array.Empty<object>()
    };

    /// <summary>เทียบเท่า BuffPageManager.Names — ID (เป็น string) -> ชื่อ buff ที่แสดงใน tooltip</summary>
    public static readonly Dictionary<string, string> Names = new()
    {
        ["76"] = "Fortunekiss", ["78"] = "Kim", ["77"] = "SP", [""] = "Red Booster",
        ["271"] = "str_biscuit_stick", ["272"] = "agi_biscuit_stick", ["273"] = "vit_biscuit_stick",
        ["274"] = "dex_biscuit_stick", ["275"] = "int_biscuit_stick", ["276"] = "luk_biscuit_stick",
        ["3"] = "Improve Concentration", ["72"] = "Bragi's Poem", ["115"] = "Falcon Eyes", ["116"] = "Wind Walker",
        ["352"] = "Fear Breeze", ["429"] = "Swing Dance", ["715"] = "Frigg's Song", ["722"] = "No Limits",
        ["1252"] = "Calamity Gale", ["1256"] = "Mystic Symphony", ["1261"] = "Musical Interlude",
        ["1262"] = "Jawaii Serenade", ["1263"] = "Prontera March",

        ["9"] = "Angelus", ["15"] = "Impositio Manus", ["20"] = "Magnificat", ["1160"] = "Powerful Faith",
        ["1161"] = "Sincere Faith", ["1162"] = "Firm Faith", ["410"] = "Rising Dragon",
        ["425"] = "Gentle Touch - Save", ["426"] = "Gentle Touch - Opposite", ["427"] = "Gentle Touch - Alive",
        ["86"] = "Vigor Explosion", ["21"] = "Gloria", ["716"] = "Offertorium", ["1201"] = "Competentia",

        ["1"] = "Endure", ["2"] = "Two hand Quicken", ["103"] = "Aura Blade", ["104"] = "Parry",
        ["105"] = "Concentration", ["107"] = "Frenzy", ["316"] = "Enchant Blade", ["319"] = "Turisus Runestone",
        ["320"] = "Hagalas Runestone", ["322"] = "Asir Runestone", ["1154"] = "Lux Runestone",

        ["1178"] = "Vigor", ["1172"] = "Servant Weapon", ["58"] = "Auto Guard", ["59"] = "Shield Reflect",
        ["62"] = "Defending Aura", ["68"] = "Spear Quicken", ["197"] = "Shrink", ["391"] = "Vanguard Force",
        ["400"] = "Exceed Break", ["402"] = "Prestige", ["407"] = "Inspiration", ["1202"] = "Guard Stance",
        ["1203"] = "Attack Stance", ["1204"] = "Guardian Shield", ["1217"] = "Rebound Shield",
        ["1220"] = "Holy Shield", ["1316"] = "Shield Spell",

        ["23"] = "Adrenaline Rush", ["24"] = "Weapon Perfection", ["25"] = "Power Thrust",
        ["26"] = "Maximize Power", ["30"] = "Crazy Uproar", ["117"] = "Shattering Strike",
        ["118"] = "Cart Boost", ["188"] = "Maximum Power-Thrust", ["361"] = "Acceleration",
        ["461"] = "Geneticist Cart Boost", ["1248"] = "Research Report", ["1249"] = "Create Hell Tree",

        ["65"] = "AUTOSPELL", ["31"] = "Energy Coat", ["127"] = "Foresight", ["186"] = "Double Bolt",
        ["113"] = "Mystical Amplification", ["198"] = "Sight Blaster", ["355"] = "Recognized Spell",
        ["717"] = "Intensification", ["1152"] = "Climax",

        ["7"] = "Poison React", ["114"] = "EDP", ["334"] = "Hallucination Walk", ["337"] = "Weapon Blocking",
        ["120"] = "REJECTSWORD", ["1192"] = "Shadow Exceed", ["1245"] = "Abyss Slayer",
        ["1193"] = "Dancing Knife", ["1194"] = "Potent Venom", ["1226"] = "Enchanting Shadow",
        ["1244"] = "From the Abyss", ["181"] = "Preserve", ["1243"] = "Abyss Dagger",

        ["758"] = "Platinum Altar", ["759"] = "HEAT BARREL", ["1345"] = "Intensive Aim",
        ["1347"] = "[Lv 1]: Gives water property to grenade", ["1348"] = "[Lv 2]: Gives wind property to grenade",
        ["1349"] = "[Lv 3]: Gives earth property to grenade", ["1350"] = "[Lv 4]: Gives fire property to grenade",
        ["1351"] = "[Lv 5]: Gives shadow property to grenade", ["1352"] = "[Lv 6]: Gives holy property to grenade",
        ["1353"] = "Auto Firing Launcher", ["1354"] = "Hidden Card",

        ["93"] = "[Lv 1] Earth ", ["92"] = "[Lv 2] Wind", ["91"] = "[Lv 3] Water ", ["90"] = "[Lv 4] Fire ",
        ["148"] = "[Lv 5] Ghost", ["146"] = "[Lv 6] Shadow", ["17"] = "[Lv 7] Holy",
        ["1039"] = "Universal Stance", ["1392"] = "Enchanting Sky", ["143"] = "Tumbling",

        ["920"] = "Fresh Shrimp", ["1367"] = "Marine Festival of Kisul", ["1368"] = "Sandy Festival of Kisul",
        ["1377"] = "Temporary Communion", ["1370"] = "[Lv 1] : Endows water property",
        ["1371"] = "[Lv 2] : Endows wind property", ["1372"] = "[Lv 3] : Endows earth property",
        ["1373"] = "[Lv 4] : Endows fire property", ["1374"] = "[Lv 5] : Endows shadow property",
        ["1375"] = "[Lv 6] : Endows holy property",

        ["206"] = "Cicada Skin Shed", ["208"] = "Ninja Aura", ["652"] = "16th Night",
        ["10"] = "Increase AGI Scroll", ["12"] = "Blessing Scroll ", ["0"] = "Aloe Vera",
        ["867"] = "Limit Power Booster", ["37"] = "Concentration Potion", ["38"] = "Awakening Potion",
        ["39"] = "Berserk Potion", ["295"] = "Abrasive", ["491"] = "Savage BBQ",
        ["492"] = "Warg Blood Cocktail", ["493"] = "Minor Brisket", ["494"] = "Siroma Icetea",
        ["495"] = "Drosera Herb Stew", ["496"] = "Petite Tail Noodles",

        ["1383"] = "Breaking Limit", ["1384"] = "Rule Break", ["1365"] = "Soul of Heaven and Earth",
        ["19"] = "RG Golden Potion", ["685"] = "almighty", ["250"] = "Battle Manual",
        ["312"] = "Job Battle Manual", ["1169"] = "Golden X Potion", ["1170"] = "Red Herb Activator",
        ["1171"] = "Blue Herb Activator", ["898"] = "Water Elemental Converter",
        ["899"] = "Earth Elemental Converter", ["900"] = "Fire Elemental Converter",
        ["901"] = "Wind Elemental Converter", ["904"] = "Cursed Water", ["908"] = "Coldproof Potion",
        ["909"] = "Earthproof Potion", ["910"] = "Fireproof Potion", ["911"] = "Thunderproof Potion",

        ["FullProtection"] = "Full Protection", ["shadow"] = "Shadow Armor", ["undead"] = "Undead Armor",
        ["ghosting"] = "Ghosting Armor",

        ["252"] = "Bubble Gum", ["BubbleGumHE"] = "Bubble Gum HE", ["1065"] = "Infinity Drink",
        ["18"] = "Holy Armor"
    };
}
