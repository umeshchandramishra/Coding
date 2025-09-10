using System.Collections;

namespace NavigaFlowPro.App;

public class ModelSettings
{
    public static int canvaswidth { get; set; }
    public static int canvasheight { get; set; }
    public static double columnWidth { get; set; }
    public static double gutterWidth { get; set; }
    public static double lineHeight { get; set; }
    public static int extraimagecaptionline { get; set; } = 1;
    public static int articleseparatorheight { get; set; } = 4;
    public static int sectionheadheight { get; set; } = 3;
    public static int hassectionfooter { get; set; } = 0;
    public static int hassectionheader { get; set; } = 0;
    public static bool disableSectionHeaderIfOverlapsAd { get; set; } = false;
    public static int hasbyline { get; set; } = 0;
    public static int haskicker { get; set; } = 0;
    public static int extralineforPreamble { get; set; } = 0; //Needs to be added to the ModelSettings
    public static int maxImagesUsed { get; set; } = 3;
    public static int maxFactCitUsed { get; set; } = 1;
    public static int extralineForByline { get; set; } = 1;
    public static int highPriorityArticleStart { get; set; }
    public static string[] largeheadersections { get; set; } = new string[0];
    public static Hashtable boxsettings { get; set; } = new Hashtable();
    public static Hashtable fullwidthfactsettings { get; set; } = new Hashtable();
    public static int extraheadlineline { get; set; } = 1;
    public static Hashtable invalidColSettings { get; set; } = new Hashtable();
    public static bool bCropAllowed { get; set; } = false;
    public static double croppercentage { get; set; } = 0.1;
    public static bool partialHeadlinesAllowed { get; set; } = true;
    public static DoubleTruckSettings clsDoubleTruck { get; set; } = new DoubleTruckSettings();
    public static int hasdoubletruck { get; set; } = 1;
    public static int hasmultispread { get; set; } = 1;
    public static int minSpaceBetweenAdsAndStories { get; set; } = 2;
    public static int minSpaceBetweenFooterAndStories { get; set; } = 0;
    public static int extrafactline { get; set; } = 0;
    public static int extraquoteline { get; set; } = 0;
    public static int minimumlinesunderImage { get; set; } = 3;
    public static int mandatoryListOrder { get; set; } = 0;
    public static bool newPageEnabled { get; set; } = false;
    public static bool enableNewPageToAArticle { get; set; } = true;
    public static int multiColumnFactMinLines { get; set; } = 10;
    public static WhiteSpaceSettings clsWhiteSpaceSettings { get; set; } = new WhiteSpaceSettings();
    public static int extralineaboveHeadline { get; set; } = 0;
    public static bool optimizeArticlePermutations { get; set; } = false;
    public static int minarticleForOptimization { get; set; } = 5;
    public static bool hasloftarticle { get; set; } = false;
    public static bool enablefactsatbottom { get; set; } = false;
    public static bool samesizesubimageallowed { get; set; } = false;
    public static bool morelayoutsformultifacts { get; set; } = false;
    public static List<string> multiFactStackingOrderPreference { get; set; } = new List<string>();
    public static Dictionary<int, int> multiColumnFactMinHeights { get; set; } = new Dictionary<int, int>();
    public static bool allowSquareOff { get; set; } = false;
    public static int minSpaceForSquaringOff { get; set; } = 0;
    public static Dictionary<string, ArticleTypeSettings> articletypesettings { get; set; } = new Dictionary<string, ArticleTypeSettings>();
    public static CustomSectionHeader clsSectionheader { get; set; } = new CustomSectionHeader();
    public static MultiSpreadSettings multiSpreadSettings { get; set; } = new MultiSpreadSettings();
    public static CustomSectionFooter clsSectionFooter { get; set; } = new CustomSectionFooter();
    public static Dictionary<string, Dictionary<string, int>> byLineOverride { get; set; } = new Dictionary<string, Dictionary<string, int>>();
    public static bool enablePlacementFromTopRight { get; set; } = false;
    public static List<SectionMapping> sectionMappingList { get; set; } = new List<SectionMapping>();
    public static BriefSettings briefSettings { get; set; } = new BriefSettings();
    public static LetterSettings letterSettings { get; set; } = new LetterSettings();
    public static Dictionary<string, List<string>> fillerAlignment { get; set; } = new Dictionary<string, List<string>>();
    public static Dictionary<string, int> squareOffThresholdOverride { get; set; } = new Dictionary<string, int>();
    public static bool bPlacingFillerAllowed { get; set; } = false;
    public static bool bAllowPlacingFillerAboveTheAd { get; set; } = false;
    public static int minimumSpaceInAdAndFiller { get; set; } = 1;
    public static PictureArticleSettings clsPictureArticleSettings { get; set; } = new PictureArticleSettings();
    public static PictureArticleDTSettings clsPictureArticleDTSettings { get; set; } = new PictureArticleDTSettings();
    public static bool picturestoriesenabled { get; set; } = false;
    public static bool picturestoriesdtenabled { get; set; } = false;
    public static bool enableNewLayoutsBelowHeadline { get; set; } = false;
    public static List<string> LayoutPreferenceOrder { get; set; } = [];
    public static BelowHeadlineLayoutSettings belowHeadlineLayoutSettings { get; set; } = new();
    public static bool generatePngFiles { get; set; } = false;
    public static bool placelowerpriorityarticleatleftorright { get; set; } = false;
    public static bool enableArticleJumps { get; set; } = false;
    public static int allowedStaticBoxSpacingY { get; set; } = 2;
    public static double minPageAreaPercentageThreshold { get; set; } = 5;
    public static bool imagemetadatasupported { get; set; } = false;
    public static bool imagemetadatasupportedDT { get; set; } = false;
    public static bool enableDTGraphicalSubImages { get; set; } = false;

    public static JumpSettings jumpArticleSettings { get; set; } = new JumpSettings();
    public static bool enableTextOverset { get; set; } = false;
    public static int MaxPermutationsPerPage { get; set; } = 10000000;
    public static bool enableNewAlgorithmForPictureStory = true;
    public static OversetRules oversetRules { get; set; } = new OversetRules();

    public static Rewards rewards { get; set; } = new Rewards();
    public static bool bTextWrapEnabled { get; set; } = false;
    public static int extraLineForTextWrapping { get; set; } = 1;
    public static TextWrapArticles textwrapSettings { get; set; } = new TextWrapArticles();
    public static List<string> SupportedImageTypes { get; set; } = new List<string> { "x-im/image" };
    public static List<string> SupportedQuoteTypes { get; set; } = new List<string> { "x-stuff/x-pullquote" };
    public static List<string> SupportedContentTypes { get; set; } = new List<string> { "x-im/content-part" };
    public static bool bExtendAdToTheBottom { get; set; } = false;
    public static bool softcropEnabled { get; set; } = false;
    public static Mugshot mugshotSetting { get; set; } = new Mugshot();

    public static bool quarterpageAdEnabled { get; set; } = false;
    public static List<PictureLeadArticleType> pictureLeadArticleTypes = new List<PictureLeadArticleType>();
    public static bool enableLargeHeadlinesForLowPriority = false;
    public static bool placeImageAtCenter = false;

    public static int sidebarWidth = 2;

    public class JumpSettings
    {
        public List<String> jumpSections { get; set; } = new List<String>();
        public int linesofOversetAllowed { get; set; } = 0;
        public string defaultHeadline { get; set; } = "small";
        public List<String> headline { get; set; } = new List<String>();
        public List<String> image { get; set; } = new List<String>();
        public List<String> factbox { get; set; } = new List<String>();
        public List<String> citation { get; set; } = new List<String>();
        public List<String> kicker { get; set; } = new List<String>();

        public List<String> preamble { get; set; } = new List<String>();
        public List<String> byline { get; set; } = new List<String>();
    }
    public class WhiteSpaceSettings
    {
        public bool addwhitespacelines { get; set; } = true;
        public int minwhitespacelines { get; set; } = 5;
        public int maxwhitespacelines { get; set; } = 5;
    }

    public static List<Penalties> lstPenalties { get; set; } = new List<Penalties>();

    public static PlacementRules placementRules { get; set; } = new PlacementRules();
    public static bool bCustomPlacementEnabled { get; set; } = false;

    public class OversetRules
    {
        public int minTextLines { get; set; }
        public int oversetPercentage { get; set; } = 20;

        public int oversetPenalty { get; set; } = 25;
        public List<OversetSectionRules> sectionrules { get; set; } = new List<OversetSectionRules>();
    }
    public class OversetSectionRules
    {
        public List<string> sections { get; set; } = new List<string>();
        public List<string> priority { get; set; } = new List<string>();
    }
    public class TextWrapArticles
    {
        public List<string> layout { get; set; } = new List<string> { "topfullheadline" };
        public int maximages { get; set; } = 1;
        public List<string> headlinetype { get; set; } = new List<string> { "large", "medium", "small" };
        public List<int> headlinesize { get; set; } = new List<int> { 2, 3 };
        public int maxStories { get; set; } = 1;
        public int maxpermutationsForOptimization { get; set; } = 10000;
        public int linesbetweenHeadlineAndAd { get; set; } = 3;

        public int maximagesPartialHeadline { get; set; } = 1;
        public int removelinesbelowAd { get; set; } = 0;
        public int maxwhitespaceAllowedBeforeTriggerAdwrap { get; set; } = -1;
    }

    public class PictureLeadArticleType
    {
        public required string type { get; set; }
        public required List<int> size { get; set; }
        public List<int>? hlsize { get; set; }
        public List<int>? imagesize { get; set; }

        public required List<string> headline { get; set; }
    }
}

public class BelowHeadlineLayoutSettings
{
    public bool AllLayoutsEnabled { get; set; } = true;
    public List<string> LayoutsEnabled { get; set; } = [];
    public bool IgnoreIfImageMetadataDefined { get; set; } = false;
    public List<BelowHeadlineLayoutRules> LayoutRules { get; set; } = [];
}

public class BelowHeadlineLayoutRules
{
    public List<string> Layouts { get; set; } = [];
    public int SubImageWidthTolerance { get; set; } = int.MaxValue;
    public int SubImageHeightTolerance { get; set; } = int.MaxValue;
}

public class SectionMapping
{
    public string section { get; set; }
    public string targetSection { get; set; }
    public bool hasPatternDefined { get; set; } = false;
    public string pattern { get; set; }
    public bool placeSectionHeaderIfNotFullPageAd { get; set; } = true;
    public List<string> exclude { get; set; } = new List<string>();
    public SectionMapping(string key, string value, string pattern)
    {
        section = key;
        targetSection = value;
        hasPatternDefined = (pattern != null);
        this.pattern = pattern;

    }

}
public class CustomSectionHeader
{
    public Dictionary<String, int> defaultSectionHeaderheight { get; set; } = new Dictionary<String, int>();
    public Dictionary<String, int> firstlargeSectionHeader { get; set; } = new Dictionary<String, int>();
    public Dictionary<String, int> firsttwolargeSectionHeader { get; set; } = new Dictionary<String, int>();
    public Dictionary<String, int> lastlargeSectionHeader { get; set; } = new Dictionary<String, int>();
}

public class CustomPageSectionFooter
{
    public List<int> PageNumbers { get; set; } = new List<int>();
    public SectionFooter Footer { get; set; }
}

public class CustomSectionFooter
{
    public Dictionary<String, SectionFooter> firstSectionFooter { get; set; } = new Dictionary<String, SectionFooter>();
    public Dictionary<String, SectionFooter> firsttwoSectionFooter { get; set; } = new Dictionary<String, SectionFooter>();
    public Dictionary<String, SectionFooter> lastSectionFooter { get; set; } = new Dictionary<String, SectionFooter>();
    public Dictionary<String, SectionFooter> firstLeftSectionFooter { get; set; } = new Dictionary<String, SectionFooter>();
    // Stores multiple page numbers per section 
    public Dictionary<string, CustomPageSectionFooter> customPageSectionFooter { get; set; } = new Dictionary<String, CustomPageSectionFooter>();
    public Dictionary<String, SectionFooter> fixedLocationFooter { get; set; } = new Dictionary<String, SectionFooter>();
}
public class BoxSettings
{
    public HashSet<int> articleLengths { get; set; } = new HashSet<int>();
    public HashSet<int> factsizes { get; set; } = new HashSet<int>();
    public HashSet<int> excludeColumnForNonAdPages { get; set; } = new HashSet<int>();
    public int minimages { get; set; }
    public int maximages { get; set; }
    public int mainmincolumns { get; set; }
    public int mainmaxcolumns { get; set; }
    public int submincolumns { get; set; }
    public int submaxcolumns { get; set; }
    public int minfactsAvailable { get; set; }
    //public int maxfactsize;
    public int maxcitationsize { get; set; }
    public string headlinetype { get; set; } = "";
}

public class FullWidthFactSettings
{
    public HashSet<int> factSizes { get; set; } = new HashSet<int>();
    public int minHeight { get; set; }
}

public class BriefSettings
{
    public bool overset { get; set; } = true;
    public int width { get; set; } = 1;
}

public class LetterSettings
{
    public string imageAlignment { get; set; } = "center";
    public int imageSize { get; set; } = 2;
    public List<string> headlineSizes { get; set; } = new List<string> { "small" };
}

public class ArticleTypeSettings
{
    public itemsize articlesize { get; set; } = new itemsize();
    public List<itemsize> imagesizes { get; set; } = new List<itemsize>();
}

public class DoubleTruckSettings
{
    public int minimages { get; set; }
    public int maximages { get; set; }
    public int mainmincolumns { get; set; }
    public int mainmaxcolumns { get; set; }
    public int submincolumns { get; set; }
    public int submaxcolumns { get; set; }
    public int minfactsAvailable { get; set; }
    public int maxcitationsize { get; set; } = 1;
    public int textminsize { get; set; }
    public int textmaxsize { get; set; }
    public HashSet<int> factsizes { get; set; } = new HashSet<int>();
    public double croppercentage { get; set; } = 0.20;
    public double maincroppercentage { get; set; } = 0.10;
    public string layouttype { get; set; } = "partialheadline";
    public HashSet<int> headlinesizes { get; set; } = new HashSet<int>();
    public RelatedArticles clsRelatedArticles { get; set; } = new RelatedArticles();
    public int mainarticleminsize { get; set; } = 6;
    public int mainarticlemaxsize { get; set; } = 12;
    public HashSet<string> headlinetypes { get; set; } = new HashSet<string>() { "large", "medium", "small" };
    public bool mainImageCaptionOnLeftPage { get; set; } = false;
    public bool hasPictureStories { get; set; } = true;
    public class RelatedArticles
    {
        public int maxarticles { get; set; } = 3;
        public HashSet<int> availablecanvasizes { get; set; } = new HashSet<int>();
        public HashSet<int> exludearticleSizes { get; set; } = new HashSet<int>();
        public bool excludemultipleOneSize { get; set; } = false;
    }
    public Dictionary<int, int> maxheadlinesize { get; set; } = new Dictionary<int, int>();
    public bool enableimagesorting { get; set; } = false;


}

public class itemsize
{
    public int width { get; set; }
    public int height { get; set; }
    public int x { get; set; }
    public int y { get; set; }
}

public class MultiSpreadSettings
{
    public int Version { get; set; } = 1;
    public List<string> HeadlineSizes { get; set; } = new List<string> { "small" };
    public int MinImagesPerSpread { get; set; } = 3;
    public int MaxImagesPerSpread { get; set; } = 7;
    public int MinFactPerSpread { get; set; } = 1;
    public int MainImageMinColumns { get; set; } = 6;
    public int MainImageMaxColumns { get; set; } = 10;
    public int SubImageMinColumns { get; set; } = 2;
    public int SubImageMaxColumns { get; set; } = 4;
    public double MainImageCropPercentage { get; set; } = 0.20;
    public double SubImageCropPercentage { get; set; } = 0.25;
    public int MinLinesUnderImage { get; set; } = 3;
    public HashSet<int> FactboxSizes { get; set; }
    public int MaxCitationSize { get; set; } = 1;
    public int ExtraImageCaptionLines { get; set; } = 0;
    public int MaxIgnoredImages { get; set; } = 0;
    public int WhitespaceTolerance { get; set; } = 0;
    public int MinimumTextBlockHeight { get; set; } = 0;
    public MultiSpreadSinglePageSettings StartPageSettings { get; set; } = new MultiSpreadSinglePageSettings();
    public MultiSpreadSinglePageSettings EndPageSettings { get; set; } = new MultiSpreadSinglePageSettings();
}

public class MultiSpreadSinglePageSettings
{
    public int MinImages { get; set; } = 2;
    public int MaxImages { get; set; } = 3;
    public int MainImageMinColumns { get; set; } = 3;
    public int MainImageMaxColumns { get; set; } = 5;
    public int SubImageMinColumns { get; set; } = 2;
    public int SubImageMaxColumns { get; set; } = 4;
    public double MainImageCropPercentage { get; set; } = 0.25;
    public double SubImageCropPercentage { get; set; } = 0.25;
}

public interface IPictureArticleSettings
{
    int MainImageMinColumns { get; }
    int MainImageMaxColumns { get; }
    int SubImageMinColumns { get; }
    int SubImageMaxColumns { get; }
    int MaxIgnoredImages { get; }
    double MaxCropPercentage { get; }
    double CropPercentage { get; }
    int MinImages { get; }
    int MaxImages { get; }
    List<string> HeadlineSizes { get; }
    List<int> TextColumns { get; }
    int MaxExploringTimePerPictureStory { get; }
    List<string> PreferredLayoutType { get; }
    bool EnableImageDistribution { get; }
    int MaxSpaceAllowedBelowText { get; }
    bool UseMaxCropping { get; }
    int SingleColumnImagePenalty { get; }
    int ExtraLineForTextWrapping { get; }
    double LayoutSpaceCoverage { get; }
    int MaxImagesBelowTextBelowHeadLine { get; }
    int MaxImagesBelowTextAboveHeadline { get; }
    int MaxAllowableEmptyLine { get; }
    double MaxSingleColumnRatio { get; }
    double MaxAllowableEmptySpace { get; }
    int PreferredLayoutTypeReward { get; }
    int CrossJoinStep { get; }
    int MaxAllowedCombinationsInDefaultCrossJoin { get; }
    double MinFloorTolerance { get; }
    List<int> partialHeadlineColumns { get; }
    double EditorialCoverageInTopPartialRect { get; }
    public List<HeadlineSize> MaxHeadlineSize { get; }
}
public class PictureArticleSettings : IPictureArticleSettings
{
    public int MainImageMinColumns { get; set; } = 2;
    public int MainImageMaxColumns { get; set; } = 3;
    public int SubImageMinColumns { get; set; } = 1;
    public int SubImageMaxColumns { get; set; } = 3;
    public int MaxIgnoredImages { get; set; }
    public double MaxCropPercentage { get; set; } = 0.30;
    public double CropPercentage { get; set; } = 0.15;
    public int MinImages { get; set; } = 6;
    public int MaxImages { get; set; } = 10;
    public List<string> HeadlineSizes { get; set; } = new List<string> { "large", "medium", "small" };
    public List<int> TextColumns { get; set; } = new List<int> { ModelSettings.canvaswidth };
    public int MaxExploringTimePerPictureStory { get; set; } = 30; // seconds
    public List<string> PreferredLayoutType { get; set; } = new List<string> { LayoutType.belowheadline.ToString() };
    public bool EnableImageDistribution { get; set; } = false;
    public int MaxSpaceAllowedBelowText { get; set; } = 3;
    public bool UseMaxCropping { get; set; } = false;
    public int SingleColumnImagePenalty { get; set; } = 0;
    public int ExtraLineForTextWrapping { get; set; } = 0;
    public double LayoutSpaceCoverage { get; set; } = 0.9;
    public int MaxImagesBelowTextBelowHeadLine { get; set; } = 4;
    public int MaxImagesBelowTextAboveHeadline { get; set; } = 2;
    public int MaxAllowableEmptyLine { get; set; } = 2;
    public double MaxSingleColumnRatio { get; set; } = 0.2;
    public double MaxAllowableEmptySpace { get; set; } = 0.01;
    public int PreferredLayoutTypeReward { get; set; } = 40; // all preferred (at postion first ) will get extra reward 
    public bool FreeCropLastImages { get; set; } = true;
    public int CrossJoinStep { get; set; } = 2;
    public int MaxAllowedCombinationsInDefaultCrossJoin { get; set; } = 2000;
    public double MinFloorTolerance { get; set; } = 0.05;
    public List<int> partialHeadlineColumns { get; set; } = new List<int> { 2, 3, 4 };
    public double EditorialCoverageInTopPartialRect { get; set; } = 0.99;
    public List<HeadlineSize> MaxHeadlineSize { get; set; } = new List<HeadlineSize> {
        new HeadlineSize { Columns = 2, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 3, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 4, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 5, Typolines = ModelSettings.canvaswidth }
    };

}
public class PictureArticleDTSettings : IPictureArticleSettings
{
    public int MainImageMinColumns { get; set; }
    public int MainImageMaxColumns { get; set; }
    public int SubImageMinColumns { get; set; }
    public int SubImageMaxColumns { get; set; }
    public int MaxIgnoredImages { get; set; }
    public double MaxCropPercentage { get; set; } = 0.20;
    public double CropPercentage { get; set; } = 0.15;
    public int MinImages { get; set; }
    public int MaxImages { get; set; }
    public List<string> HeadlineSizes { get; set; } = new List<string> { "large", "medium", "small" };
    public List<int> TextColumns { get; set; } = new List<int> {1,2,3,4};
    public int MaxExploringTimePerPictureStory { get; set; } = 20;
    public List<string> PreferredLayoutType { get; set; } = new List<string> { "belowHeadline" };
    public int MaxSpaceAllowedBelowText { get; set; } = 6;
    public bool EnableImageDistribution { get; set; } = false;
    public bool UseMaxCropping { get; set; } = false;
    public int SingleColumnImagePenalty { get; set; } = 0;
    public int ExtraLineForTextWrapping { get; set; } = 0;
    public double LayoutSpaceCoverage { get; set; } = 0.9;
    public int MaxImagesBelowTextBelowHeadLine { get; set; } = 5;
    public int MaxImagesBelowTextAboveHeadline { get; set; } = 3;
    public int MaxAllowableEmptyLine { get; set; } = 4;
    public double MaxSingleColumnRatio { get; set; } = 0.2;
    public double MaxAllowableEmptySpace { get; set; } = 0.01;
    public int PreferredLayoutTypeReward { get; set; } = 40; // all preferred (at postion first ) will get extra reward 
    public int SearchDepth { get; set; } = 2;
    public int CrossJoinStep { get; set; } = 3;
    public int MaxAllowedCombinationsInDefaultCrossJoin { get; set; } = 5000;
    public double MinFloorTolerance { get; set; } = 0.05;
    public bool allowSubImageOnSpine { get; set; } = false;
    public List<int> partialHeadlineColumns { get; set; } = new List<int> { 3, 4, 5, 6 };
    public double EditorialCoverageInTopPartialRect { get; set; } = 0.95;
    public List<HeadlineSize> MaxHeadlineSize { get; set; } = new List<HeadlineSize> {
        new HeadlineSize { Columns = 2, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 3, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 4, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 5, Typolines = ModelSettings.canvaswidth },
        new HeadlineSize { Columns = 6, Typolines = ModelSettings.canvaswidth }
    };
}

public class HeadlineSize
{
    public int Columns { get; set; }
    public int Typolines { get; set; }
}

public class Mugshot
{
    public int maxMugshotAllowed { get; set; } = 2;
    public string mugshotAlignment { get; set; } = "right";
}

public class Penalties
{
    public int penaltyperc { get; set; }
    public int priority { get; set; }
    public int size { get; set; }

}
public class Rewards
{
    public List<articleimagesizegreatherthan> lstarticleimagesizeReward { get; set; } = new List<articleimagesizegreatherthan>();
    public List<fixedWidthImageSizes> lstFixedWidthImage { get; set; } = new List<fixedWidthImageSizes>();
    public List<headlinetypolinesgreaterthan> lsthltypolineReward { get; set; } = new List<headlinetypolinesgreaterthan>();
    public Layoutpreference layoutpreference { get; set; }

    public RewardOnArticleWidthPreference rewardOnArticleWidthPreference {get; set;} = new RewardOnArticleWidthPreference();    
    public class articleimagesizegreatherthan
    {
        public int priority { get; set; }
        public int size { get; set; }
        public int reward { get; set; }

    }
    public class fixedWidthImageSizes
    {
        public int priority { get; set; }
        public int reward { get; set; }

    }

    public class headlinetypolinesgreaterthan
    {
        public int priority { get; set; }
        public int typolines { get; set; }
        public int reward { get; set; }

    }

    public class Layoutpreference
    {
        public string evenpagelayout { get; set; }
        public string oddpagelayout { get; set; }
        public int reward { get; set; } = 0;
        public int minimumimagesinlayout { get; set; } = 3;
        public bool singlearticleonpage { get; set; } = true;
			
    }

    public class RewardOnArticleWidthPreference
    {
        public int MaxWidthPreference { get; set; } = 0;
        public int AverageWidthPreference { get; set; } = 0;
    }

}

public class PlacementRules
{
    public string defaultoddpage { get; set; }
    public string defaultevenpage { get; set; }
    public List<SectionPlacemntRules> oddpagerule { get; set; } = new List<SectionPlacemntRules>();
    public List<SectionPlacemntRules> evenpagerule { get; set; } = new List<SectionPlacemntRules>();
}

public class SectionPlacemntRules
{
    public string section { get; set; }
    public string rule { get; set; }
}

