using System;
using System.Collections.Generic;
using System.Windows.Media;


namespace LeitostrapV7.Core;


public class StaticTheme
{
    public string Name { get; set; } = "";
    public string BgMain { get; set; } = "";
    public string BgCard { get; set; } = "";
    public string BgHover { get; set; } = "";
    public string Accent { get; set; } = "";
    public string BgNav { get; set; } = "";
    public string Border { get; set; } = "";
}


public class AnimatedTheme
{
    public string Name { get; set; } = "";
    public string Accent { get; set; } = "";
    public string Gradient { get; set; } = "";
    public string Anim { get; set; } = "";
}


public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();


    public List<StaticTheme> StaticThemes { get; } = new();
    public List<AnimatedTheme> AnimatedThemes { get; } = new();


    private ThemeService()
    {
        LoadThemes();
    }


    private void LoadThemes()
    {
        var staticData = new[]
        {
            ("Default", "#000000", "#0f0f0f", "#1c1c1c", "#ffffff", "#060606", "#1a1a1a"),
            ("Pure Black", "#000000", "#0a0a0a", "#141414", "#e0e0e0", "#030303", "#151515"),
            ("Carbon", "#080808", "#111111", "#1a1a1a", "#B0B0B0", "#050505", "#1c1c1c"),
            ("Midnight Blue", "#0a0e1a", "#141c33", "#1e2d4d", "#5BC0BE", "#070a12", "#1a2540"),
            ("Deep Navy", "#060c18", "#0c1830", "#142850", "#4FC3F7", "#040810", "#0f1e38"),
            ("Crimson Red", "#140404", "#280808", "#400e0e", "#E63946", "#0c0202", "#301010"),
            ("Blood Moon", "#120308", "#240610", "#3a0c1a", "#FF1744", "#0a0204", "#2a0814"),
            ("Forest", "#061208", "#0c2410", "#143a1c", "#2A9D8F", "#040c06", "#0e2012"),
            ("Emerald", "#041410", "#082820", "#104030", "#06D6A0", "#030c0a", "#0a2418"),
            ("Neon Cyberpunk", "#0a0020", "#140040", "#200060", "#F72585", "#060014", "#180050"),
            ("Hot Pink", "#180410", "#300820", "#4c1030", "#FF69B4", "#100208", "#380a28"),
            ("Sunset", "#1a0a04", "#331408", "#50200c", "#F4A261", "#120602", "#3a180a"),
            ("Amber", "#140c04", "#281808", "#40280c", "#FFB300", "#0c0802", "#301c0a"),
            ("Royal Gold", "#141204", "#282408", "#40380c", "#FFD166", "#0c0c02", "#302c0a"),
            ("Amethyst", "#100614", "#200c28", "#301440", "#9D4EDD", "#0a0410", "#281030"),
            ("Deep Violet", "#0c0418", "#180830", "#281050", "#7C3AED", "#080310", "#200c40"),
            ("Charcoal", "#060606", "#0e0e0e", "#181818", "#555555", "#040404", "#1a1a1a"),
            ("Silver", "#0e1012", "#1c2024", "#2c343c", "#8D99AE", "#0a0c0e", "#242a30"),
            ("Ocean Teal", "#041418", "#082830", "#104050", "#48CAE4", "#030c10", "#0a2428"),
            ("Deep Sea", "#020a14", "#041428", "#082040", "#0077B6", "#010610", "#061830"),
            ("Coral", "#16080c", "#2c1018", "#441c24", "#FF8FAB", "#100508", "#341420"),
            ("Rose", "#140810", "#281020", "#401c30", "#E879A0", "#0c060a", "#301428"),
            ("Sapphire", "#04081a", "#081034", "#0c1c54", "#2196F3", "#030612", "#0a1440"),
            ("Electric Blue", "#040a18", "#081430", "#0c2050", "#00D4FF", "#030810", "#0a1840"),
            ("Ruby", "#160408", "#2c0810", "#440c18", "#D90429", "#100204", "#340a14"),
            ("Obsidian", "#080808", "#121212", "#1e1e1e", "#6C757D", "#050505", "#1c1c1c"),
            ("Mocha", "#120c08", "#241810", "#3c2818", "#A87042", "#0c0806", "#2c1e14"),
            ("Cinnamon", "#140a06", "#28140c", "#402014", "#CD853F", "#0c0804", "#301a10"),
            ("Indigo Ice", "#060a1a", "#0c1434", "#142054", "#4CC9F0", "#040812", "#0e1840"),
            ("Plum", "#100814", "#201028", "#301c40", "#7209B7", "#0a0610", "#281430"),
            ("Electric Lime", "#081404", "#102808", "#1c400c", "#B5E48C", "#060c03", "#14300a"),
            ("Toxic Green", "#041404", "#082808", "#10400c", "#39FF14", "#030c02", "#0a300a"),
            ("Mango", "#141204", "#282408", "#40380c", "#FFB703", "#0c0c02", "#302c0a"),
            ("Mint", "#041412", "#082824", "#104038", "#74C69D", "#030c0c", "#0a2820"),
            ("Cobalt", "#04061a", "#080c34", "#0c1454", "#3A0CA3", "#030412", "#0a1040"),
            ("Onyx", "#000000", "#060606", "#101010", "#2B2D42", "#000000", "#0e0e0e"),
            ("Olive", "#101408", "#202810", "#34401c", "#606C38", "#0c0e06", "#283014"),
            ("Velvet", "#0c0018", "#180030", "#280050", "#5A189A", "#080010", "#200040"),
            ("Steel", "#08101a", "#102034", "#1c3454", "#457B9D", "#060c12", "#142840"),
            ("Rose Gold", "#140c10", "#281820", "#402830", "#B56576", "#0c0a0c", "#301c28"),
            ("Slate", "#0c1012", "#182024", "#28343c", "#6D6875", "#0a0c0e", "#1e2830"),
            ("Pearl", "#161616", "#2c2c2c", "#444444", "#F8F9FA", "#101010", "#383838"),
            ("Hot Magenta", "#16040e", "#2c081c", "#440c2c", "#FF006E", "#100208", "#340a24"),
            ("Lime Burst", "#0a1404", "#142808", "#20400c", "#C8FF00", "#080c03", "#1c300a"),
            ("Cyber Blue", "#040c18", "#081830", "#102850", "#00D4FF", "#030a10", "#0a1c40"),
            ("Dark Amber", "#140c04", "#281808", "#40280c", "#FF9F1C", "#0c0802", "#301c0a"),
            ("Frost", "#0a0a0e", "#14141c", "#22222e", "#E0F7FA", "#08080c", "#1a1a24"),
            ("Deep Crimson", "#120000", "#240000", "#3a0000", "#DC143C", "#0c0000", "#2c0000"),
            ("Neon Green", "#001404", "#002808", "#00400c", "#39FF14", "#000c02", "#00300a"),
            ("Titanium", "#0c0c0c", "#181818", "#282828", "#E8E8E8", "#080808", "#222222"),
            ("Cosmic Purple", "#08001a", "#100034", "#1c0054", "#BF40BF", "#060012", "#140040"),
            ("Ocean Depth", "#000814", "#001028", "#001c44", "#00509E", "#000610", "#001430"),
            ("Cherry", "#140406", "#28080c", "#401014", "#DE3163", "#0c0204", "#300a10"),
            ("Lavender", "#0e0a16", "#1c1430", "#2e2050", "#B39DDB", "#0a0810", "#241c40"),
            ("Peach", "#160e0a", "#2c1c14", "#442c20", "#FFAB91", "#100a08", "#34221a"),
            ("Arctic", "#080e14", "#101c28", "#1c2e44", "#80DEEA", "#060a10", "#142434"),
            ("Sunrise", "#160c06", "#2c180c", "#442814", "#FF8A65", "#100a04", "#341e10"),
            ("Midnight", "#040612", "#080c24", "#10143c", "#7986CB", "#030410", "#0a1030"),
        };


        foreach (var (name, bgMain, bgCard, bgHover, accent, bgNav, border) in staticData)
            StaticThemes.Add(new StaticTheme { Name = name, BgMain = bgMain, BgCard = bgCard, BgHover = bgHover, Accent = accent, BgNav = bgNav, Border = border });


        var animatedData = new[]
        {
            ("Black Hole", "#9C27B0", "radial-gradient(circle, #1a0030, #000005)", "blackhole"),
            ("Crimson Vortex", "#FF1744", "radial-gradient(circle, #2a0008, #050002)", "vortex"),
            ("Toxic Swamp", "#76FF03", "linear-gradient(180deg, #001a00, #0a2a05)", "toxic"),
            ("Deep Space", "#4FC3F7", "radial-gradient(circle, #000818, #000208)", "deepspace"),
            ("Blood Moon", "#D50000", "radial-gradient(circle, #2a0000, #080000)", "bloodmoon"),
            ("Northern Lights", "#00E5FF", "linear-gradient(45deg, #001a1a, #003040)", "aurora"),
            ("Supernova", "#FF6D00", "radial-gradient(circle, #3a1000, #0a0200)", "supernova"),
            ("Frozen Abyss", "#80D8FF", "linear-gradient(180deg, #001020, #0a2040)", "frozen"),
            ("Void Walker", "#7C4DFF", "radial-gradient(circle, #140030, #040010)", "void"),
            ("Solar Flare", "#FFD600", "radial-gradient(circle, #2a2000, #0a0800)", "solar"),
            ("Haunted Forest", "#4CAF50", "linear-gradient(180deg, #001a08, #0a1a0a)", "forest"),
            ("Acid Burn", "#C6FF00", "linear-gradient(135deg, #0a1a00, #1a2a00)", "acid"),
            ("Storm Chaser", "#00BCD4", "linear-gradient(180deg, #000a14, #001830)", "storm"),
            ("Molten Core", "#FF3D00", "linear-gradient(180deg, #2a0400, #5a0a00)", "molten"),
            ("Phantom Mist", "#B0BEC5", "linear-gradient(180deg, #0a0a0e, #1a1a24)", "phantom"),
            ("Nebula Drift", "#E040FB", "radial-gradient(circle, #200040, #080010)", "nebula"),
            ("Cyber Storm", "#00E676", "linear-gradient(90deg, #001a10, #003020)", "cyber"),
            ("Volcanic Ash", "#FF5722", "linear-gradient(180deg, #1a0400, #4a0a00)", "volcanic"),
            ("Crystal Cave", "#80DEEA", "linear-gradient(45deg, #001520, #0a3050)", "crystal"),
            ("Dark Matter", "#311B92", "radial-gradient(circle, #0a0020, #020008)", "darkmatter"),
            ("Wildfire", "#FF9100", "linear-gradient(180deg, #1a0800, #4a1800)", "wildfire"),
            ("Arctic Blast", "#E0F7FA", "linear-gradient(135deg, #001018, #0a2838)", "arctic"),
            ("Radioactive Waste", "#76FF03", "radial-gradient(circle, #001a00, #000a00)", "radioactive"),
            ("Twilight Zone", "#CE93D8", "linear-gradient(45deg, #140020, #280040)", "twilight"),
        };


        foreach (var (name, accent, gradient, anim) in animatedData)
            AnimatedThemes.Add(new AnimatedTheme { Name = name, Accent = accent, Gradient = gradient, Anim = anim });
    }
}
