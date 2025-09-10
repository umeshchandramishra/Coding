using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

using NavigaFlowPro.App.ArticleLayouts.LayoutEngine;
using NavigaFlowPro.App.BelowHeadlineLayouts;
using NavigaFlowPro.App.MultiSpreads.LayoutEngine;
using NavigaFlowPro.App.MultiSpreads.LayoutEngine.Data;
using NavigaFlowPro.App.MutiFactLayouts;

using NavigaFlowProApplication;

using Serilog;
using Serilog.Core;

using static System.Collections.Specialized.BitVector32;

namespace NavigaFlowPro.App;

internal class FlowProClass
{
    string sTempLocation;
    string sJsonDirectory = "";
    string srunName = "";
    public static string sPubName = "";
    string sfullrunName = "";
    string sOutputJsonFile = "";
    string sHostDetailsFile = "";
    string sAuditFile = "";
    string sOutputErrorFile = "";
    string s3bucket = "";
    string sOutputKey = "";
    private int _numpermutations = 10;
    public static string LOGFILENAME = "";
    public static int canvasx = 0;
    public static int canvasz = 0;


    private List<Box> articles;
    private List<ScoreList> finalscores = new List<ScoreList>();
    private List<FinalScores> newfinalscores = new List<FinalScores>();
    private List<ScoreList> finalscores1 = new List<ScoreList>();

    private Hashtable headlines;
    private Hashtable kickersmap;
    public static Hashtable lstImageCaption = new Hashtable();
    private Hashtable headlineMap = new Hashtable();
    Hashtable articlePermMap = new Hashtable();
    //Hashtable articlePermMapOriginal = new Hashtable();
    Dictionary<BigInteger, List<ScoreList>> NewarrScoreList = new Dictionary<BigInteger, List<ScoreList>>();

    private HashSet<BigInteger> lstArticlesAddedOnNextPage = new HashSet<BigInteger>();
    ArrayList lstPageSection = new ArrayList();

    List<PageInfo> lstPages = new List<PageInfo>();
    List<ScoreList> _globalfulllist;
    RegionEndpoint awsregionendpoint = null;

    List<Filler> fillers = new List<Filler>();
    Dictionary<KeyValuePair<double, double>, Filler> availableFillers = new Dictionary<KeyValuePair<double, double>, Filler>();
    private BoxArticlePositions dictArticlePositions = new BoxArticlePositions();

    private Dictionary<string, Jumps> dictJumpSettings = new Dictionary<string, Jumps>();
    private bool bArticleJumpsSection = false;
    private List<String> lstArticlePriority = new List<String>() { "A", "B", "C", "D", "E" };
    private string cropIdentifier = "im://crop/";
    private FlowAudit audit;
    private List<string> unplacedReason = new List<string>();
    private bool enableLogginOfUnplacedBoxCombinations = true;

    public FlowProClass(string zip, AppSettings settings)
    {
        //Download the s3 to local path
        s3bucket = "";
        sTempLocation = settings.TempLocation;
        string _guid = Guid.NewGuid().ToString();

        // Create working directory
        sTempLocation = Path.Combine(sTempLocation, _guid) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(sTempLocation);
        Directory.CreateDirectory(settings.LogFileFolder);

        // Extract ZIP
        ZipFile.ExtractToDirectory(Path.Combine(settings.TempLocation, zip), sTempLocation);

        sfullrunName = Path.GetFileNameWithoutExtension(zip);

        LOGFILENAME = Path.Combine(settings.LogFileFolder, sfullrunName + ".Log");

        // Get first directory from extracted structure
        string[] _dir = Directory.GetDirectories(sTempLocation);
        DirectoryInfo _dirInfo = new DirectoryInfo(_dir[0]);
        srunName = _dirInfo.Name;

        // JSON directory path
        sJsonDirectory = Path.Combine(sTempLocation, srunName, srunName + "Json") + Path.DirectorySeparatorChar; ;

        sOutputKey = sfullrunName + "_output.zip";
        sPubName = GetPubName();
    }
    //overloaded this only for CLI purpose
    public FlowProClass(string[] args, AppSettings settings)
    {
        if (args == null || args.Length < 4)
        {
            throw new ArgumentException("Invalid arguments. Usage: flow.exe <input.zip path> <Blank> <MT setting path> <output folder path>");
        }

        // Parse command-line arguments
        string zipPath = args[0];
        string mtPath = args[2];
        // Validate that the zip file exists
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException($"The specified zip file does not exist: {zipPath}");
        }

        if (!File.Exists(mtPath))
        {
            throw new FileNotFoundException($"The specified file does not exist: {mtPath}");
        }

        // Initialize member variables
        s3bucket = "";
        string _guid = Guid.NewGuid().ToString();

        // Create temp location and log directory
        sTempLocation = Path.Combine(settings.TempLocation, _guid) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(sTempLocation);
        Directory.CreateDirectory(settings.LogFileFolder);

        // Extract ZIP
        ZipFile.ExtractToDirectory(zipPath, sTempLocation);

        sfullrunName = Path.GetFileNameWithoutExtension(zipPath);

        // Set log file path
        LOGFILENAME = Path.Combine(settings.LogFileFolder, sfullrunName + ".Log");

        // Get first directory from extracted structure
        string[] _dir = Directory.GetDirectories(sTempLocation);
        if (_dir.Length == 0)
        {
            throw new InvalidOperationException("The extracted zip folder does not contain any directories.");
        }

        DirectoryInfo _dirInfo = new DirectoryInfo(_dir[0]);
        srunName = _dirInfo.Name;

        // Set JSON directory path
        sJsonDirectory = Path.Combine(sTempLocation, srunName, srunName + "Json") + Path.DirectorySeparatorChar; ;
    }

    private void ParseEditorialAds()
    {
        EditorialAdsJson editorialAdRoot = null;
        string editorialAdJson = sJsonDirectory + srunName + "ContentAdditions.json";
        // Check if the file exists
        if (System.IO.File.Exists(editorialAdJson))
        {
            try
            {
                string jsonString = System.IO.File.ReadAllText(editorialAdJson);
                editorialAdRoot = JsonSerializer.Deserialize<EditorialAdsJson>(jsonString) ?? new();

            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while processing the JSON file: " + ex.Message);
            }
            if (editorialAdRoot == null)
                return;
            for (int i = 0; i < editorialAdRoot.EditorialAds.Count(); i++)
            {
                var items = editorialAdRoot.EditorialAds[i].Items;
                var sectionWhiteList = editorialAdRoot.EditorialAds[i].Default.SectionWhitelist;
                var sectionBlackList = editorialAdRoot.EditorialAds[i].Default.SectionBlacklist;
                for (int j = 0; j < items.Count; j++)
                {
                    var dimensions = items[j].Dimensions;
                    for (int k = 0; k < dimensions.Count; k++)
                    {
                        var adWidth = double.Parse(dimensions[k].Width);
                        var adHeight = double.Parse(dimensions[k].Height);
                        availableFillers[new KeyValuePair<double, double>(adWidth, adHeight)] = new Filler
                        {
                            x = 0,
                            original_x = 0,
                            y = 0,
                            original_y = 0,
                            Width = 0,
                            original_Width = adWidth,
                            Height = 0,
                            original_Height = adHeight,
                            canNotUsedInSection = sectionBlackList,
                            canUsedInSections = sectionWhiteList,

                            file = items[j].File
                        };
                    }
                }
            }
        }
    }

    public void LoadStaticBoxes()
    {
        foreach (var page in lstPages)
        {
            if (page.sectionheaderheight > 0)
            {
                page.staticBoxes.Add(new StaticBox(0, 0, canvasx, page.sectionheaderheight, FlowElements.Header));
            }
            if (page.footer != null && page.footer.height > 0)
            {
                page.staticBoxes.Add(new StaticBox(page.footer.x, page.footer.y, page.footer.width, page.footer.height, FlowElements.Footer));
            }
            foreach (var ad in page.ads)
            {
                page.staticBoxes.Add(new StaticBox(ad.newx, ad.newy, ad.newwidth, ad.newheight, FlowElements.Ad));
            }
            //init painted canvas, This should be last line
            page.paintedCanvas = Helper.GetPainedCanvas(page.staticBoxes);
        }
    }

    public void ProcessData(AppSettings settings,RunMode mode,string modelSettingPath = "",string outputPath = "")
    {
        sPubName = GetPubName();

        string outputDir;
        if (mode == RunMode.Debug && !string.IsNullOrWhiteSpace(outputPath))
        {
            // CLI override in Debug
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);
            outputDir = outputPath;

            sOutputJsonFile = GetUniqueFileName(Path.Combine(outputDir, $"{sfullrunName}Laydown.json"));
            sHostDetailsFile = Path.Combine(outputDir, "HostDetails.txt");
            // In original Debug you only set sAuditFile when using default temp path; keeping that intact: not setting here.
        }
        else
        {
            outputDir = Path.Combine(sTempLocation, "output");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            sOutputJsonFile = Path.Combine(outputDir, $"{sfullrunName}Laydown.json");
            sAuditFile = Path.Combine(outputDir, $"{sfullrunName}FlowAudit.json");
            sHostDetailsFile = Path.Combine(outputDir, "HostDetails.txt");
        }

        // Normal-only paths
        string sOutputZip = $"{sfullrunName}_output.zip";
        sOutputErrorFile = Path.Combine(outputDir, "error.json"); // this is a field in your class in ProcessData

        // Normal: also ensure log folder (matches your ProcessData)
        if (mode == RunMode.Normal && !Directory.Exists(settings.LogFileFolder))
            Directory.CreateDirectory(settings.LogFileFolder);

        if (mode == RunMode.Debug)
        {
            if (!string.IsNullOrWhiteSpace(modelSettingPath))
                LoadModelSettings(modelSettingPath, null, null); // CLI workflow
            else
                LoadModelSettings(null, settings, sPubName);
        }
        else
        {
            LoadModelSettings(null, settings, sPubName);
        }

        Log.Information("LoadModelSettings() completed successfully");

        try
        {
            Log.Information($"Processing started-------------------- {mode.ToString()}");

            LoadAds();
            Log.Information("LoadAds() completed successfully");

            ParseEditorialAds();
            Log.Information("ParseEditorialAds() completed successfully");

            BuildContent();
            Log.Information("BuildContent() completed successfully");

            InitFlowAudit();
            Log.Information("InitFlowAudit() completed successfully");

            LoadSectionFooter();
            Log.Information("LoadSectionFooter() completed successfully");

            SetPlacementRules();
            Log.Information("SetPlacementRules() completed successfully");

            LoadStaticBoxes();
            Log.Information("LoadStaticBoxes() completed successfully");

            SetOversetRules();
            Log.Information("SetOversetRules() completed successfully");

            BuildLayout();
            Log.Information("BuildLayout() completed successfully: {time}", DateTime.Now);

            GenerateJsonFile();
            
            GenerateAuditJson();
            
            GenerateHostDetailsFile();

            if (ModelSettings.generatePngFiles)
            {
                var pagesOutputDir = Path.Combine(outputDir, "Pages"); // same effective path as before
                Helper.GeneratePngFiles(sOutputJsonFile, pagesOutputDir);
            }

            Log.Information("GenerateJsonFile() completed successfully");

            // Normal-only: zip + S3
            if (mode == RunMode.Normal && File.Exists(sOutputJsonFile))
            {
                string zipOutputPath = Path.Combine(sTempLocation, sOutputZip);
                ZipFile.CreateFromDirectory(outputDir, zipOutputPath, CompressionLevel.Fastest, includeBaseDirectory: false);
                CopyZipFileToS3(zipOutputPath);
                Log.Information("CopyZipFileToS3() completed successfully");
            }
        }
        catch (Exception e)
        {
            if (mode == RunMode.Debug)
            {
                // Match Debug: log only, do not rethrow, no error.json/zip/S3, no cleanup
                Log.Error("Error occurred while processing Flow: {msg}", e.Message);
                return;
            }

            // Normal-mode error handling (match your ProcessData)

            Log.Error("Error occurred while processing Flow: {msg}", e.Message);
            GenerateErrorJsonFile(sOutputErrorFile, e.Message + " " + e.StackTrace);
            GenerateHostDetailsFile();

            Log.Information("GenerateJsonFile() completed successfully");

            if (File.Exists(sOutputErrorFile))
            {
                string zipOutputPath = Path.Combine(sTempLocation, sOutputZip);
                ZipFile.CreateFromDirectory(outputDir, zipOutputPath, CompressionLevel.Fastest, includeBaseDirectory: false);
                CopyZipFileToS3(zipOutputPath);
            }

            try
            {
                Directory.Delete(sTempLocation, true);
            }
            catch
            {
                Log.Error("Couldn't delete the temp directory: {dir}", sTempLocation);
            }

            throw; // preserve original rethrow in Normal
        }
        finally
        {
            // Normal cleans temp; Debug keeps artifacts (match originals)
            if (mode == RunMode.Normal)
            {
                try
                {
                    Directory.Delete(sTempLocation, true);
                }
                catch
                {
                    Log.Error("Couldn't delete the temp directory: {dir}", sTempLocation);
                }
            }
        }
    }
   
    static string GetUniqueFileName(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        string directory = Path.GetDirectoryName(filePath);
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);

        int counter = 1;
        string newFilePath;
        do
        {
            newFilePath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
            counter++;
        } while (File.Exists(newFilePath));

        return newFilePath;
    } 
    public FlowProClass(string sbucket, string skey, Logger staticFileLogger, AppSettings settings)
    {
        //Download the s3 to local path
        s3bucket = sbucket;
        string _guid = Guid.NewGuid().ToString();
        sTempLocation = Path.Combine(settings.TempLocation, _guid) + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(sTempLocation);
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.AWSRegion))
            {
                awsregionendpoint = RegionEndpoint.GetBySystemName(settings.AWSRegion);
            }

        }
        catch (Exception e)
        {
            staticFileLogger.Error("Error while fetch RegionEndPoint: {message}", e.Message);
            //Log.LogHelper.Log(Log.LogHelper.LogTarget.File, "Error while fetch RegionEndPoint:" + e.Message);
        }

        AmazonS3Client s3Client = null;
        if (awsregionendpoint != null)
            s3Client = new AmazonS3Client(awsregionendpoint);
        else
            s3Client = new AmazonS3Client();

        staticFileLogger.Information("Client created: {bucket}", sbucket);
        //Log.LogHelper.Log(Log.LogHelper.LogTarget.File, "Client created:" + sbucket);
        ListObjectsRequest request = new ListObjectsRequest();

        ListObjectsResponse response = s3Client.ListObjectsAsync(request.BucketName = sbucket, request.Prefix = skey).Result;
        string zipname = "";
        foreach (S3Object obj in response.S3Objects)
        {
            try
            {
                zipname = Path.GetFileName(obj.Key);
                var fileTransferUtility = new TransferUtility(s3Client);
                string localZipPath = Path.Combine(sTempLocation, zipname);
                fileTransferUtility.Download(localZipPath, sbucket, obj.Key);
            }
            catch (Exception ex)
            {
                staticFileLogger.Error("Download failed from S3: {message}", ex.Message);
                //Log.LogHelper.Log(Log.LogHelper.LogTarget.File, "Download failed from S3: " + ex.Message);
                throw ex;
            }
        }

        string zipFilePath = Path.Combine(sTempLocation, zipname);
        ZipFile.ExtractToDirectory(zipFilePath, sTempLocation);
        sfullrunName = zipname.Replace("_input.zip", "");
        LOGFILENAME = Path.Combine(settings.LogFileFolder, sfullrunName + ".Log");
        //sTempLocation = "C:\\temp\\";
        string[] _dir = Directory.GetDirectories(sTempLocation);
        DirectoryInfo _dirInfo = new DirectoryInfo(_dir[0]);
        srunName = _dirInfo.Name;
        sJsonDirectory = Path.Combine(sTempLocation, srunName, srunName + "Json") + Path.DirectorySeparatorChar; ;

        string[] arr = skey.Split(new string[] { "Input/" }, StringSplitOptions.RemoveEmptyEntries);
        sOutputKey = Path.Combine(arr[0], "Output", sfullrunName + "_output.zip").Replace("\\", "/");
        sPubName = GetPubName();
    }

    public string GetPubName()
    {
        string _pubname = ""; ;
        foreach (string sfilename in Directory.EnumerateFiles(sJsonDirectory))
        {
            if (sfilename.Contains("DailyDials-"))
            {
                string _sdailydials = System.IO.File.ReadAllText(sfilename);
                using (JsonDocument _doc = JsonDocument.Parse(_sdailydials))
                {
                    _pubname = _doc.RootElement.GetProperty("publication").ToString();
                }
                break;
            }
        }
        return _pubname;
    }
    
    private void InitFlowAudit()
    {
        List<string> charprops = new List<string>() { "X", "E", "D", "C", "B", "A" };
        audit = new FlowAudit
        {
            StoryList = new List<AuditStory>()
        };
        foreach (var box in articles)
        {
            var story = new AuditStory
            {
                ArticleId = box.Id,
                NewPage = box.isNewPage ? "yes" : (ModelSettings.enableNewPageToAArticle && box.priority == 5) ? "yes" : "no",
                Placed = "no",
                Reason = "",
                Title = headlineMap.ContainsKey(box.Id) ? headlineMap[box.Id].ToString() : "",
                Section = box.category ?? "",
                Priority = charprops[box.priority],
                PageNumber = "",
                Dt = box.isdoubletruck ? "yes" : "no",
                Mt = box.spreadPageCount > 0 ? "yes" : "no",
            };
            audit.StoryList.Add(story);
        }
    }
    private void GenerateAuditJson()
    {
        string fileName = sAuditFile;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(audit, options);
        File.WriteAllText(fileName, json);

        Log.Information($"Audit file written to {fileName}");

    }
    private void CopyZipFileToS3(string _outputzipfile)
    {
        try
        {
            AmazonS3Client s3Client = null;
            if (awsregionendpoint != null)
                s3Client = new AmazonS3Client(awsregionendpoint);
            else
                s3Client = new AmazonS3Client();
            var fileTransferUtility = new TransferUtility(s3Client);

            fileTransferUtility.Upload(_outputzipfile, s3bucket, sOutputKey);
        }
        catch (Exception e)
        {
            Log.Error("Message: {msg}", e.Message);
        }

    }
    //NewPage as page-break.
    void AssignArticlesToPages(List<Box> filteredArticles, List<PageInfo> lstFilteredPages, int mandatoryListOrderSection)
    {
        int pageCount = 0;
        int indexCounter = 0;

        while (mandatoryListOrderSection == 1 && pageCount < lstFilteredPages.Count)
        {
            int firstIndex = filteredArticles.FindIndex(indexCounter, article => article.isNewPage);
            if (firstIndex == -1)
            {
                break;
            }
            int secondIndex = filteredArticles.FindIndex(firstIndex + 1, article => article.isNewPage);
            if (secondIndex == -1)
            {
                secondIndex = filteredArticles.Count;
            }
            for (int i = firstIndex; i < secondIndex; i++)
            {
                filteredArticles[i].page = lstFilteredPages[pageCount].pageid;
                filteredArticles[i].pagesname = lstFilteredPages[pageCount].sname;
                lstFilteredPages[pageCount].articleList.Add(filteredArticles[i].Id);
            }

            pageCount++;
            indexCounter = secondIndex;
        }
    }
    private void BuildLayout()
    {
        DateTime d1 = DateTime.Now;
        DateTime d = DateTime.Now;
        try
        {
            Log.Information("BuildLayout started - {time}", DateTime.Now);
            if (ModelSettings.enableArticleJumps)
                BuildJumpFrontPage();

            foreach (string _section in lstPageSection)
            {
                bArticleJumpsSection = ModelSettings.enableArticleJumps;
                List<Box> filteredarticles = articles.Where(arts => arts.category.Equals(_section, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => -1 * x.boxorderId).ToList();
                List<PageInfo> lstFilteredPages = lstPages.Where(sect => sect.section.Equals(_section, StringComparison.OrdinalIgnoreCase) && !sect.ignorepage).OrderBy(x => x.sname).ThenBy(x => x.pageid).ToList();

                //Remove the FrontPage from this List
                lstFilteredPages.RemoveAll(x => x.bFrontPage);

                if (lstFilteredPages.Count() == 0)
                {
                    Log.Information("BuildLayout - No pages Found for Section: {section}", _section);
                    continue;
                }

                if (filteredarticles.Exists(x => x.isjumparticle && x.origArea <= 0))
                    filteredarticles.RemoveAll(x => x.isjumparticle && x.origArea <= 0);

                if (filteredarticles.Count == 0)
                {
                    Log.Information("BuildLayout - No articles Found for Section: {section}", _section);
                    continue;
                }

                var candidateArticles = filteredarticles.Where(x => x.isdoubletruck ||
                x.articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase) ||
                x.spreadPageCount > 0).ToList();

                foreach (var article in candidateArticles.Where(a => a.imageList?.Any() == true))
                {
                    article.imageList.RemoveAll(img => img.imagetype == "mugshot");
                }

                //FLOW-184
                if (ModelSettings.mandatoryListOrder == 1)
                {
                    if (ModelSettings.newPageEnabled == true && ModelSettings.enableNewPageToAArticle == true)
                    {
                        Log.Information("Setting A article as page break, along with the existing page break");
                        filteredarticles.Where(x => x.priority == 5).ToList().ForEach(x => x.isNewPage = true);
                    }
                    else if (ModelSettings.newPageEnabled == true && ModelSettings.enableNewPageToAArticle == false)
                    {
                        //Make only first article as newPage(regardless of the priority)
                        Log.Information("New mandatory order list will be used along with first page as page-break");
                        filteredarticles.OrderBy(x => x.rank).First().isNewPage = true; // Rank indicate article order in which it is passed in story JSON
                    }
                    else if (ModelSettings.newPageEnabled == false && ModelSettings.enableNewPageToAArticle == true)
                    {
                        Log.Information("Old mandatory order list going to be used ");
                        filteredarticles.ForEach(x => x.isNewPage = false);
                        filteredarticles.Where(x => x.priority == 5).ToList().ForEach(x => x.isNewPage = true);

                        //NT - could be written like this:
                        //filteredarticles.ForEach(x => x.isNewPage = x.priority == 5);
                    }
                    else if (ModelSettings.newPageEnabled == false && ModelSettings.enableNewPageToAArticle == false)
                    {
                        Log.Information("Nothing going to place for section : {section}, enableNewPageToAArticle & newPageEnabled is false", _section);
                        continue; // Do nothing
                    }
                }
                else
                {
                    Log.Information("MandatoryOrderList disabled : Old algorithms will be used");
                }
                Log.Information("BuildLayout - Section processing started for {section}-{time}", _section, DateTime.Now);

                //POK: Calaculate the available area in all the pages
                for (int _i = 0; _i < lstFilteredPages.Count(); _i++)
                {
                    //POK: SEACTIONFOOTER HANDLINE NEEDS TO BE DONE LATER
                    int _pagearea = canvasx * canvasz;
                    int _pagelength = canvasz;
                    //substrat sectionheader
                    //Umesh: FLOW-291 {Section Header placed only if not a Full Page Ad}
                    if (ModelSettings.hassectionheader == 1)
                    {
                        _pagearea = _pagearea - canvasx * lstFilteredPages[_i].sectionheaderheight;
                        _pagelength = _pagelength - lstFilteredPages[_i].sectionheaderheight;
                    }

                    if (lstFilteredPages[_i].footer != null)
                    {
                        _pagearea = _pagearea - lstFilteredPages[_i].footer.width * lstFilteredPages[_i].footer.height;
                    }

                    if (lstFilteredPages[_i].ads != null && lstFilteredPages[_i].ads.Count() > 0)
                    {
                        foreach (var _ad in lstFilteredPages[_i].ads)
                        {
                            _pagearea = _pagearea - _ad.newwidth * _ad.newheight;
                        }
                        //Pokh: This doesn't take care of the vertical ads. Needs to be revisted
                        //_pagelength = _pagelength - lstFilteredPages[_i].ads[0].newheight;
                    }

                    lstFilteredPages[_i]._availablepagearea = _pagearea;
                    lstFilteredPages[_i]._pagearea = _pagearea;
                    //Keeping 20% area for images
                    //lstFilteredPages[_i]._availablepagearea = (int)(lstFilteredPages[_i]._availablepagearea * 0.8);
                    lstFilteredPages[_i]._availablelngth = _pagelength;
                }

                //Remove the page where no space left w.r.t. threshold provided in MT
                lstFilteredPages.RemoveAll(x => x._pagearea <= (ModelSettings.minPageAreaPercentageThreshold / 100) * canvasx * canvasz);


                List<Box> dtArticles = filteredarticles.Where(x => x.isdoubletruck == true && x.articletype.ToLower() != "picturestories").OrderBy(x => x.rank).ToList();
                dtArticles.ForEach(x => x.priority = 5);

                filteredarticles = filteredarticles.OrderBy(a => a.rank).ToList();
                int mandatoryListOrderSection = ModelSettings.mandatoryListOrder;

                if (mandatoryListOrderSection == 1)
                {
                    //if mandatory list order is true, we need to ignore the parent article being passed.
                    //It should be based off the list order.
                    filteredarticles.ForEach(x => x.parentArticleId = "");
                    foreach (var _dtitem in dtArticles)
                    {
                        int _dtrank = _dtitem.rank;

                        int nextNewpageArticleRank = 10000;
                        var nextNewpageArticle = filteredarticles.Find(x => x.rank > _dtrank && x.isNewPage == true);
                        if (null != nextNewpageArticle)
                        {
                            nextNewpageArticleRank = nextNewpageArticle.rank;
                        }
                        foreach (var item in filteredarticles.Where(x => x.rank > _dtrank && x.rank < nextNewpageArticleRank))
                            item.parentArticleId = _dtitem.Id;
                    }
                }

                Log.Information("Pages Count: {pageCount}", lstFilteredPages.Count);
                Log.Information("Articles Count: {articleCount}", filteredarticles.Count(a => a.priority == 5));
                Log.Information("DT Articles Count: {dtArticleCount}", dtArticles.Count());
                Log.Information("Mandatory List order value: {mandatoryOrderValue}", mandatoryListOrderSection);

                if (ModelSettings.hasmultispread == 1)
                {
                    if (mandatoryListOrderSection == 1)
                    {
                        List<Box> aarticleList = null;

                        if (ModelSettings.newPageEnabled)
                            aarticleList = filteredarticles.Where(x => x.isNewPage == true).OrderBy(x => x.rank).ToList();
                        else
                            aarticleList = filteredarticles.Where(x => x.priority == 5).OrderBy(x => x.rank).ToList();

                        var skipPages = 0;
                        for (int _icnt = 0; _icnt < aarticleList.Count(); _icnt++)
                        {
                            if (aarticleList[_icnt].spreadPageCount <= 0)
                            {
                                skipPages += aarticleList[_icnt].isdoubletruck ? 2 : 1;
                                continue;
                            }

                            Box _dtarticle = aarticleList[_icnt];
                            var pageCount = _dtarticle.spreadPageCount;

                            List<PageInfo> selectedPages = lstFilteredPages.Skip(skipPages).Take(pageCount).ToList();
                            var firstPage = selectedPages.FirstOrDefault();
                            var lastPage = selectedPages.LastOrDefault();

                            bool canprint = true;

                            if (selectedPages.Count < pageCount) // checking for whether there are enough pages for multispread
                            {
                                canprint = false;
                                Log.Warning("Can't print multispread Article: {id}, it requires {count} pages", _dtarticle.Id, pageCount);
                            }

                            if (canprint && lastPage.pageid != firstPage.pageid + _dtarticle.spreadPageCount - 1)
                            {// the pages should be continous
                                canprint = false;
                                Log.Warning("Can't print multispread Article: {id} because pages are discontinous. Start page id: {fpageId}, End page id: {lpageId}", _dtarticle.Id, firstPage.pageid, lastPage.pageid);
                            }

                            if (canprint)
                            {
                                Log.Information("Trying to print multispread Article: {id}. Start page id: {fpageId}, End page id: {lpageId}", _dtarticle.Id, firstPage.pageid, lastPage.pageid);
                                var hasFit = false;
                                if (ModelSettings.multiSpreadSettings.Version == 1)
                                {
                                    hasFit = new MultiSpread(_dtarticle, selectedPages, headlines, kickersmap).GenerateMultiSpreadLayout();
                                }
                                else
                                {
                                    hasFit = MultiSpreadLayoutEngine.Generate(_dtarticle, selectedPages, headlines, kickersmap);
                                }

                                if (hasFit)
                                    Log.Information("Multispread Article: {id} printed successfully", _dtarticle.Id);
                                else
                                    Log.Warning("Unable to generate any layout for multispread Article: {id}", _dtarticle.Id);
                            }

                            filteredarticles.RemoveAll(x => x.Id == _dtarticle.Id);
                            filteredarticles.RemoveAll(x => x.parentArticleId == _dtarticle.Id);
                            aarticleList.RemoveAll(x => x.Id == _dtarticle.Id);
                            selectedPages.ForEach(page => lstFilteredPages.Remove(page));
                            _icnt--;
                        }
                    }
                    else
                    {
                        foreach (var article in filteredarticles)
                        {
                            if (article.spreadPageCount <= 0) continue;

                            var eligiblePages = new List<PageInfo>();
                            var pageCount = article.spreadPageCount;

                            for (int i = 0; i <= lstFilteredPages.Count - pageCount; i++)
                            {
                                var selection = lstFilteredPages.Skip(i).Take(pageCount);
                                if (selection.All(x => x.ads == null || x.ads.Count == 0)) // Checking whether any of the selected pages have Ads
                                {
                                    if (selection.Last().pageid == (selection.First().pageid + article.spreadPageCount - 1))
                                    { // Checking whether the selected pages are continous or not
                                        eligiblePages.AddRange(selection);
                                        break;
                                    }
                                }
                            }

                            if (eligiblePages.Count() > 0)
                            {
                                var hasFit = false;
                                if (ModelSettings.multiSpreadSettings.Version == 1)
                                {
                                    hasFit = new MultiSpread(article, eligiblePages, headlines, kickersmap).GenerateMultiSpreadLayout();
                                }
                                else
                                {
                                    hasFit = MultiSpreadLayoutEngine.Generate(article, eligiblePages, headlines, kickersmap);
                                }

                                if (hasFit)
                                {
                                    eligiblePages.ForEach(page => lstFilteredPages.Remove(page));
                                }
                                filteredarticles.Remove(article);
                            }
                            else
                            {
                                Log.Information("Can't print multispread Article:" + article.Id);
                            }
                        }
                    }
                }

                BuildPictureStoriesSpread(filteredarticles, lstFilteredPages, mandatoryListOrderSection, _section);

                BuildDoubleTruckPages(filteredarticles, lstFilteredPages, mandatoryListOrderSection);

                BuildPictureStories(filteredarticles, lstFilteredPages, mandatoryListOrderSection);


                AssignArticlesToPages(filteredarticles, lstFilteredPages, mandatoryListOrderSection);

                //Set the correct Jumppage on frontpage article and jumparticles
                if (ModelSettings.enableArticleJumps && filteredarticles.Exists(x => x.isjumparticle))
                {
                    Log.Information("Setting the JumpFrom on Jump Articles");
                    SetTheJumpFromToPageIds(filteredarticles);
                }

                articlePermMap = new Hashtable();

                for (int _acount = 0; _acount < filteredarticles.Count; _acount++)
                {
                    var item = filteredarticles[_acount];

                    Log.Information("Finding permutation for Id: {id}", item.Id);

                    List<Box> _boxlist = FindAllArticlePermutations(item, 0);

                    Log.Information("Found permutation for Id: {id} Count: {count}", item.Id, _boxlist.Count);

                    if (ModelSettings.LayoutPreferenceOrder.Count > 0)
                    {


                        _boxlist = [.. _boxlist.OrderByDescending(x => x.width)
                            .ThenByDescending(x => x.length)
                            .ThenByDescending(x => x.usedimagecount)
                            .ThenBy(x =>
                            {
                                var index = ModelSettings.LayoutPreferenceOrder.FindIndex(name => name.Equals(x.layouttype.ToString(), StringComparison.OrdinalIgnoreCase));
                                return index >= 0 ? index : int.MaxValue;
                            })
                            .ThenBy(x => x.whitespace)
                            .ThenBy(x => x.headlinelength)];
                    }
                    else
                    {
                        _boxlist = [.. _boxlist.OrderByDescending(x => x.width)
                            .ThenByDescending(x => x.length)
                            .ThenByDescending(x => x.usedimagecount)
                            .ThenBy(x => x.whitespace)
                            .ThenBy(x => x.headlinelength)];
                    }

                    PageInfo _articlePage = lstFilteredPages.Where(x => x.pageid == item.page && x.sname == item.pagesname).FirstOrDefault();

                    if (ModelSettings.rewards.layoutpreference == null)
                        Log.Information("layoutpreference is null");
                    if (ModelSettings.rewards.layoutpreference != null && _articlePage != null
                        && _articlePage.articleList.Count == 1 && item.imageList != null)
                        Helper.RemoveDuplicateArticleSizesUsingScore(_boxlist, item, _articlePage);
                    else
                        Helper.RemoveDuplicateArticleSizes(_boxlist);

                    if (_boxlist.Count > 0)
                        articlePermMap.Add(item.Id, _boxlist);
                    else
                    {
                        filteredarticles.Remove(item);
                        _acount--;
                    }

                    Log.Information("Item Id: {id} Priority: {priority}", item.Id, item.priority);
                    foreach (var _item in _boxlist)
                    {
                        Log.Information("----Width, Length, UsedImages, UsedAboveImages, LayoutType, LayoutName: {w},{l},{usedImg},{usedAboveImg},{ltype},{lname}",
                            _item.width, _item.length, _item.usedimagecount, _item.usedaboveimagecount, _item.layouttype, _item.layout);
                        if (_item.usedImageList != null)
                        {
                        }
                    }
                }

                List<Box> aarticlelist = filteredarticles.Where(cust => cust.priority == 5).ToList();
                List<Box> barticlelist = barticlelist = filteredarticles.Where(cust => cust.priority == 4).ToList();
                List<Box> carticlelist = filteredarticles.Where(cust => cust.priority < ModelSettings.highPriorityArticleStart).ToList();
                newfinalscores = new List<FinalScores>();

                List<Box> _allhighlist = new List<Box>();
                _allhighlist.AddRange(aarticlelist);
                _allhighlist.AddRange(barticlelist);

                foreach (var _article in filteredarticles)
                {
                    HashSet<int> _availableLengths = _article.avalableLengths();
                    foreach (int _i in _availableLengths)
                    {
                        List<Box> _tempBoxList = (List<Box>)articlePermMap[_article.Id];
                        _tempBoxList = _tempBoxList.Where(x => x.width == _i).OrderByDescending(x => -1 * x.volume).ToList();
                        if (_tempBoxList != null && _tempBoxList.Count() > 0)
                            _article._possibleAreas.Add(_i, (int)_tempBoxList[0].volume);
                    }
                }

                Console.WriteLine("Section Generation: " + _section);
                NewarrScoreList = new Dictionary<BigInteger, List<ScoreList>>();

                if (lstFilteredPages.Count() >= 1 && (_allhighlist.Count() + carticlelist.Count()) >= 1)
                {
                    newfinalscores = BuildPage_V6(1, lstFilteredPages, _allhighlist, _section, carticlelist, 0, mandatoryListOrderSection);
                    if (newfinalscores.Count() == 0) //No match found, relax the criteria to find the match
                    {
                        Log.Information("BuildLayout - No Record Found for Section: {section}, continuing with relaxed criteria", _section);
                        NewarrScoreList = new Dictionary<BigInteger, List<ScoreList>>();
                        foreach (var _t in _allhighlist)
                        {
                            articlePermMap.Remove(_t.Id);
                        }
                        foreach (var item in _allhighlist)
                        {
                            articlePermMap.Add(item.Id, FindAllArticlePermutations(item, 1));
                        }
                        lstFilteredPages.ForEach(x => x._availablepagearea = x._pagearea);

                        newfinalscores = BuildPage_V6(1, lstFilteredPages, _allhighlist, _section, carticlelist, 0, mandatoryListOrderSection);

                    }
                }


                // newfinalscores = newfinalscores.OrderByDescending(x => x.articlesprinted).ThenByDescending(x=>x.TotalScore).ToList();
                if (newfinalscores.Count() == 0)
                {
                    Log.Information("BuildLayout - No Record Found for Section: {section}", _section);
                    continue;
                }

                if (newfinalscores != null && newfinalscores.Count() > 0)
                {
                    if (ModelSettings.bPlacingFillerAllowed)
                    {
                        TryToFitEditorialAd_OutsideStory(newfinalscores[0].lstScores, lstFilteredPages);
                    }
                    else
                    {
                        Helper.ExtendTheBottomArticles(newfinalscores[0].lstScores, lstFilteredPages);
                    }
                    if (ModelSettings.quarterpageAdEnabled)
                        Helper.RepositionTheArticles(newfinalscores[0].lstScores);

                    if (ModelSettings.extralineaboveHeadline > 0)
                        Helper.AddExtraLineBetweenHeadlineAndImages(newfinalscores[0].lstScores);
                    foreach (ScoreList _slist in newfinalscores[0].lstScores)
                    {
                        string _pid = _slist.pageid;
                        PageInfo _tmp = lstPages.Find(c => c.sname + c.pageid == _pid);
                        _tmp.sclist = _slist;
                    }

                    Log.Information("BuildLayout - Final Score for Section: {score}", newfinalscores[0].TotalScore);
                }
                Log.Information("BuildLayout - Section processing completed for {section}-{time}", _section, DateTime.Now);

            }
            Log.Information("BuildLayout completed - {time}", DateTime.Now);


        }
        catch (Exception ex)
        {
            Log.Error("BuildLayout Failed - {msg}", ex.Message);
            throw;
            //Log the Exception
        }
    }
    //UM: Editorial ads
    private void TryToFitEditorialAd_OutsideStory(List<ScoreList> scoreLists, List<PageInfo> filteredPages)
    {
        foreach (var scoreList in scoreLists)
        {
            var pageInfo = filteredPages.FirstOrDefault(p => p.sname + p.pageid == scoreList.pageid);
            if (pageInfo == null)
                continue;

            if (scoreList.boxes == null || scoreList.boxes.Count == 0)
                continue;

            int newPageHeight = Helper.CalculateNewPageHeight(pageInfo);

            var pageCoordinates = Helper.InitializePageCoordinates(pageInfo);
            //Umesh:=>  use pageInfo.paintedCanvas, instead calculating again.

            foreach (var box in scoreList.boxes)
            {
                Helper.PaintUsedArea(box, pageCoordinates);

                if (Helper.IsBoxOverlappingWithStaticBox(box, pageInfo.staticBoxes))
                    continue;

                if (Helper.IsLastBox(box, scoreList.boxes) && box.position != null)
                {
                    if (box.position.pos_z + box.length < newPageHeight)
                    {
                        int newY = (availableFillers.Count() > 0) ? TryFittingFillerBelowTheLastBox(box, pageInfo, newPageHeight) : 0;
                        if (box.articletype == "")
                        {
                            box.length = (newY > box.position.pos_z) ? newY - box.position.pos_z : newPageHeight - box.position.pos_z;
                        }
                    }
                }
            }
            TryFittingFillerIfStillSpaceLeftAnyWhereOnThePage(pageCoordinates, pageInfo);
        }
    }

    private bool HasArticleAboveTheWhiteSpace(int startX, int startY, int width, Dictionary<KeyValuePair<int, int>, bool> pageCoordinates, int sectionHeaderHeight)
    {
        for (int y = startY - 1; y > sectionHeaderHeight; y--)
        {
            for (int x = startX; x < startX + width; x++)
            {
                var key = new KeyValuePair<int, int>(x, y);
                if (pageCoordinates.ContainsKey(key) && pageCoordinates[key] == true)
                {
                    return true;
                }
            }
        }
        return false;
    }


    private void TryFittingFillerIfStillSpaceLeftAnyWhereOnThePage(Dictionary<KeyValuePair<int, int>, bool> pageCoordinate, PageInfo pageInfo)
    {
        if (availableFillers.Count() > 0)
        {
            var WhiteSpaceBoxes = GetWhiteSpaceCoordinates(pageCoordinate, ModelSettings.canvasheight);
            foreach (var ws in WhiteSpaceBoxes)
            {
                int whiteSpaceHeight = ws.height;
                if (Helper.IsBoxOverlappingWithStaticBox(ws.startX, ws.startX + ws.width, pageInfo.staticBoxes))
                {
                    if (!ModelSettings.bAllowPlacingFillerAboveTheAd) continue;
                    whiteSpaceHeight = ws.height;
                }

                if (fillers.Where(x => (int)x.x == ws.startX && (int)x.y == ws.startY && x.pageId == pageInfo.pageid).Count() > 0) continue; // overlapping with already placed fillers.

                if (!HasArticleAboveTheWhiteSpace(ws.startX, ws.startY, ws.width, pageCoordinate, pageInfo.sectionheaderheight)) continue;

                var filler = Helper.GetBestFillerForWhiteSpace(ws.startX, ws.startY, whiteSpaceHeight, ws.width, pageInfo.section, pageInfo.pageid, availableFillers);
                if (filler != null)
                {
                    fillers.Add(filler);
                }
            }
        }
    }

    private List<(int startX, int startY, int width, int height)> GetWhiteSpaceCoordinates(Dictionary<KeyValuePair<int, int>, bool> pageCoordinates, int newPageHeight)
    {
        List<(int startX, int startY, int width, int height)> whiteSpaces = new List<(int startX, int startY, int width, int height)>();

        for (int i = 1; i < newPageHeight; i++)
        {
            for (int j = 0; j < canvasx; j++)
            {
                var key = new KeyValuePair<int, int>(j, i);
                if (pageCoordinates[key] == false)
                {
                    int startX = j; int startY = i; int width = 0;
                    while (startX + width < canvasx && pageCoordinates[new KeyValuePair<int, int>(startX + width, startY)] == false)
                    {
                        width++;
                    }
                    int height = 0;
                    bool isRowEmpty = true;
                    while (startY + height < newPageHeight && isRowEmpty)
                    {
                        for (int x = startX; x < startX + width; x++)
                        {
                            if (pageCoordinates[new KeyValuePair<int, int>(x, startY + height)] == true)
                            {
                                isRowEmpty = false;
                                break;
                            }
                        }
                        if (isRowEmpty)
                        {
                            height++;
                        }
                    }
                    whiteSpaces.Add((startX, startY, width, height));
                    for (int y = startY; y < startY + height; y++)
                    {
                        for (int x = startX; x < startX + width; x++)
                        {
                            pageCoordinates[new KeyValuePair<int, int>(x, y)] = true;
                        }
                    }
                }
            }
        }
        return whiteSpaces;
    }

    private int TryFittingFillerBelowTheLastBox(Box box, PageInfo page, int newPageHeight)
    {
        var emptyAreaX = box.position.pos_x;
        var emptyAreaY = box.length + box.position.pos_z + ModelSettings.minSpaceBetweenAdsAndStories;
        var emptyAreaWidth = box.width;
        var emptyAreaHeight = newPageHeight - emptyAreaY;
        var filler = Helper.GetBestFillerForWhiteSpace(emptyAreaX, emptyAreaY, emptyAreaHeight, emptyAreaWidth, box.category, page.pageid, availableFillers);
        if (filler == null)
        {
            return 0;
        }
        fillers.Add(filler);
        return (int)Math.Round(filler.y) - ModelSettings.minSpaceBetweenAdsAndStories;
    }

    private void LoadSectionFooter()
    {
        if (ModelSettings.hassectionfooter == 1)
        {
            foreach (var _largesf in ModelSettings.clsSectionFooter.firstSectionFooter)
            {
                string _section = _largesf.Key;
                List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());

                if (_lstinfo.Count > 0)
                {
                    PageInfo _firstpage = _lstinfo[0];
                    _firstpage.footer = _largesf.Value;
                }
            }

            foreach (var _largesf in ModelSettings.clsSectionFooter.firsttwoSectionFooter)
            {
                string _section = _largesf.Key;
                List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());
                if (_lstinfo.Count > 0)
                    _lstinfo[0].footer = _largesf.Value;

                if (_lstinfo.Count > 1 && _lstinfo[0].pageid % 2 == 0 && _lstinfo[0].pageid + 1 == _lstinfo[1].pageid)
                    _lstinfo[1].footer = _largesf.Value;
            }

            foreach (var _largesf in ModelSettings.clsSectionFooter.lastSectionFooter)
            {
                string _section = _largesf.Key;
                List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());
                if (_lstinfo.Count > 0)
                {
                    PageInfo _lastpage = _lstinfo[_lstinfo.Count - 1];
                    _lastpage.footer = _largesf.Value;
                }
            }

            // Assign first left section footers
            foreach (var _largesf in ModelSettings.clsSectionFooter.firstLeftSectionFooter)
            {
                string _section = _largesf.Key.ToLower(); // Convert once to avoid repeated calls
                PageInfo firstLeftPage = lstPages.FirstOrDefault(x => x.section.ToLower() == _section && x.pageid % 2 == 0);

                if (firstLeftPage != null)
                {
                    firstLeftPage.footer = _largesf.Value;
                }
            }

            // Assign footers based on specific index positions in customPageSectionFooter
            foreach (var _largesf in ModelSettings.clsSectionFooter.customPageSectionFooter)
            {
                string _section = _largesf.Key;
                CustomPageSectionFooter customFooter = _largesf.Value;
                List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());

                foreach (int pageIndex in customFooter.PageNumbers)
                {
                    if (pageIndex >= 1 && pageIndex <= _lstinfo.Count)
                    {
                        _lstinfo[pageIndex - 1].footer = customFooter.Footer;
                    }
                    else
                    {
                        Log.Warning($"Could not assign the sectionfooter due to invalid page index {pageIndex} for section {_section}");
                    }
                }
            }

            // Assign footers based on fixed locations, like- A8, B1 etc.
            foreach (var _largesf in ModelSettings.clsSectionFooter.fixedLocationFooter)
            {
                var match = lstPages.Find(p =>
                    string.Equals($"{p.sname}{p.pageid}", _largesf.Key, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    match.footer = _largesf.Value;
                }
                else
                {
                    Log.Warning($"Could not assign SectionFooter: invalid page location '{_largesf.Key}'");
                }
            }

        }
    }
    //POK 08/25: byline area should be added to Box
    private void BuildByline(ref JsonElement rootRef, ref Box _boxRef)
    {
        int _byline;
        if (int.TryParse(rootRef.GetProperty("galleyDump").GetProperty("byline").ToString(), out _byline))
        {
            _boxRef.byline = (_byline > 0) ? _byline + ModelSettings.extralineForByline : 0;
        }
    }

    bool ParseImageMetadata(string imageType, Image image, bool isGraphic, bool isMugshot)
    {
        bool isMetaDataAvailable = false;
        double minWidth = 0, maxWidth = 0;
        bool allowCropValue = ModelSettings.bCropAllowed;
        double height = 0;

        foreach (var prop in imageType.Split(';').OrderBy(x => x[0]))
        {
            var keyValue = prop.Split(':').Select(s => s.Trim()).ToArray();
            if (keyValue.Length != 2) continue;

            var key = keyValue[0];
            var value = keyValue[1];
            switch (key.ToLower())
            {
                case "w":
                case "width":
                    if (Regex.IsMatch(keyValue[1], @"^\d+(\.\d+)?C?$|^\d+(\.\d+)?-\d+(\.\d+)?C?$", RegexOptions.IgnoreCase))
                    {
                        if (isMugshot)
                        {
                            (minWidth, maxWidth) = ParseImageMetaDataWidth<double>(value);
                        }
                        else
                        {
                            (minWidth, maxWidth) = ParseImageMetaDataWidth<int>(value);
                        }
                        isMetaDataAvailable = true;
                    }
                    else
                    {
                        height = 0;
                        Log.Warning("Invalid value found for width in metadata: {Value}", value);
                    }
                    break;
                case "c":
                case "crop":
                    if (bool.TryParse(value, out bool result))
                    {
                        allowCropValue = result;
                        isMetaDataAvailable = true;
                    }
                    else
                    {
                        allowCropValue = ModelSettings.bCropAllowed;
                        Log.Warning("Invalid value found for crop in metadata: {Value}, will use MT setting", value);
                    }
                    break;
                case "h":
                case "height":
                    value = value.TrimEnd('l', 'L');
                    if (double.TryParse(value, out height))
                    {
                        allowCropValue = false;
                        isMetaDataAvailable = true;
                    }
                    else
                    {
                        height = 0;
                        Log.Warning("Invalid value found for height in metadata: {Value}", value);
                    }
                    break;
                case "g":
                case "graphic":
                case "graphics":
                    if (bool.TryParse(value, out isGraphic))
                    {
                        if (isGraphic)
                        {
                            allowCropValue = false;
                            isMetaDataAvailable = true;
                        }
                    }
                    else
                    {
                        isGraphic = false;
                        Log.Warning("Invalid value found for graphic in metadata: {Value}", keyValue[1]);
                    }
                    break;

                case "m":
                case "mugshot":
                    if (bool.TryParse(keyValue[1], out isMugshot))
                    {
                        if (isMugshot)
                            Log.Information("Image-Id {Value} is a mugshot", image.id);
                    }
                    else
                    {
                        isMugshot = false;
                        Log.Warning("Invalid value found for graphic in metadata: {Value}", keyValue[1]);
                    }
                    break;
            }
        }

        allowCropValue = !isGraphic && allowCropValue;

        //ignore height if width is either
        //  a. Not provided/invalid format so it uses MT values
        //  b. A range is provided
        if ((isGraphic || height > 0) && (minWidth != maxWidth || (minWidth == 0 && maxWidth == 0)))
        {
            height = 0;
        }
        if (isMugshot)
        {
            if (minWidth <= 0 || minWidth >= 1)
            {
                Log.Error(" Invalid mugshot width has been passed in image meta data, Image will be ignored, ImageId = " + image.id);
                return false;
            }
            image.imageMetadata = new ImageMetadata(isMugshot: true, isGraphic: false, allowCrop: false, height: 0, sizes: null, CreateMugshot(ref image, minWidth, height)); ;
        }
        else
        {
            image.imageMetadata = isMetaDataAvailable
                ? new ImageMetadata(false, isGraphic, allowCropValue, (int)height, Enumerable.Range((int)minWidth, (int)Math.Max(0, maxWidth - minWidth + 1)).ToList(), null)
                : null;
        }
        return true;
    }
    private SizeD CreateMugshot(ref Image image, double mughotWidth, double mugshotHeight)
    {
        SizeD mshotObj = new SizeD(mughotWidth, mugshotHeight, 0);
        image.imagetype = "mugshot";//change image type as placement logic of mugshot is totally different
        image.fixedWidthImage = true;
        return mshotObj;
    }

    public static (T Min, T Max) ParseImageMetaDataWidth<T>(string widthValue)
    {
        if (string.IsNullOrWhiteSpace(widthValue))
        {
            Serilog.Log.Warning("Blank or null width value");
            return (default(T), default(T));
        }

        try
        {
            var range = widthValue.TrimEnd('C', 'c').Split('-');

            if (range.Length == 2 && TryParseValue<T>(range[0], out T minWidth) && TryParseValue<T>(range[1], out T maxWidth))
            {
                return (minWidth, maxWidth);
            }

            if (range.Length == 1 && TryParseValue<T>(range[0], out T singleWidth))
            {
                return (singleWidth, singleWidth);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Exception occurred while parsing width: " + ex.Message);
            return (default(T), default(T));
        }

        return (default(T), default(T));
    }

    private static bool TryParseValue<T>(string value, out T result)
    {
        result = default(T);

        if (typeof(T) == typeof(int))
        {
            if (int.TryParse(value, out int intResult))
            {
                result = (T)(object)intResult;
                return true;
            }
        }
        else if (typeof(T) == typeof(double))
        {
            if (double.TryParse(value, out double doubleResult))
            {
                result = (T)(object)doubleResult;
                return true;
            }
        }

        return false;
    }

    private void BuildContent()
    {
        string storiesjson = sJsonDirectory + srunName + "Stories.json";
        string imagesjson = sJsonDirectory + srunName + "Images.json";

        string[] storiesstring = System.IO.File.ReadAllLines(storiesjson);
        string[] imagesstring = System.IO.File.ReadAllLines(imagesjson);

        if (imagesstring.Length == 0)
        {
            Log.Information("Image json is empty.");
        }

        if (imagesstring.Length >= 1)
        {
            if (string.IsNullOrWhiteSpace(imagesstring[0]) || imagesstring[0] == "[]" || imagesstring[0] == "{}")
            {
                Log.Warning("The entire image json file is empty.");
                imagesstring = Array.Empty<string>();
            }
        }




        articles = new List<Box>();
        headlines = new Hashtable();
        headlineMap = new Hashtable();
        kickersmap = new Hashtable();
        int _rank = 0;
        foreach (string jsonstory in storiesstring)
        {
            _rank++;

            using (JsonDocument document = JsonDocument.Parse(jsonstory))
            {
                Box _box = new Box();
                _box.rank = _rank;
                JsonElement root = document.RootElement;
                string storyid = root.GetProperty("SAXo-InternalId").GetString();

                ArticlePosition _articleposition = LoadJumpArticlePositions(root);
                if (_articleposition != null)
                {
                    dictArticlePositions.lstArticlePositions.Add(storyid, _articleposition);
                }

                _box.Id = storyid;
                _box.origArea = double.Parse(root.GetProperty("galleyDump").GetProperty("body").ToString());

                if (ModelSettings.enableArticleJumps && root.TryGetProperty("jumpArticle", out var jumparticleelement))
                {
                    if (bool.Parse(jumparticleelement.GetString()))
                    {
                        _box.isjumparticle = bool.Parse(jumparticleelement.GetString());
                        if (root.GetProperty("galleyDump").TryGetProperty("jumps", out var jumpelement))
                        {
                            Jumps _jump = LoadJumpFromToData(jumpelement);
                            dictJumpSettings.Add(storyid, _jump);
                        }
                        else
                        {
                            Log.Information("No galleydump data exist for Jump article: {id}", _box.Id);
                            _box.isjumparticle = false;
                        }

                        if (root.TryGetProperty("jumpSection", out var jumpsectionElement))
                        {
                            string _js = jumpsectionElement.GetString();
                            if (!jsonstory.Equals("Nfnd", StringComparison.OrdinalIgnoreCase))
                                _box.jumpSection = _js;
                        }
                        if (_box.jumpSection == null || _box.jumpSection.Trim().Length == 0)
                        {
                            Log.Information("No jumpsection data exist for Jump article: {id}", _box.Id);
                            _box.isjumparticle = false;
                        }
                    }
                }

                if (root.TryGetProperty("articletype", out var articletype))
                {
                    _box.articletype = articletype.GetString();

                    if (_box.articletype == null || _box.articletype.ToLower() == "nfnd")
                    {
                        _box.articletype = "";
                    }
                    else
                    {
                        _box.articletype = _box.articletype.ToLower();
                    }
                }

                if (root.TryGetProperty("articlecontent", out var articlecontent))
                {
                    string _atype = articlecontent.ToString();
                    if (_atype != null && _atype.Equals("picturelead", StringComparison.OrdinalIgnoreCase))
                    {
                        _box.articletype = _atype.ToLower();
                    }


                }
                int _preamble = 0;
                if (int.TryParse(root.GetProperty("galleyDump").GetProperty("preamble").ToString(), out _preamble))
                {
                    if (_preamble > 0)
                        _box.preamble = _preamble + ModelSettings.extralineforPreamble;
                    else
                        _box.preamble = 0;
                }
                else
                    _box.preamble = 0;
                _box.origArea += _box.preamble; //POK 08/25: preamle area should be added to Box

                bool isdoubletruck = bool.Parse(root.GetProperty("requestDoubleTruck").ToString());
                _box.isdoubletruck = isdoubletruck;
                if (root.TryGetProperty("spreadPageCount", out var _spreadPage) && int.TryParse(_spreadPage.ToString(), out var spreadpagecount))
                {
                    _box.spreadPageCount = spreadpagecount;
                }

                string _priority = root.GetProperty("StoryPriority").GetString();
                int _ipriority = 1;
                if (_priority == "A")
                    _ipriority = 5;
                if (_priority == "B")
                    _ipriority = 4;
                if (_priority == "C")
                    _ipriority = 3;
                if (_priority == "D")
                    _ipriority = 2;
                if (_priority == "E")
                    _ipriority = 1;
                _box.priority = _ipriority;
                _box.category = root.GetProperty("SAXo-Category").GetString().Trim();


                int _wordcount = 0;
                int.TryParse(root.GetProperty("Calc:Bdy:wordCount").GetString(), out _wordcount);
                _box.bodywordcount = _wordcount;
                //Umesh => FLOW-184
                if (root.TryGetProperty("newPage", out var _newPage) && bool.TryParse(_newPage.ToString(), out var newPageValue))
                {
                    _box.isNewPage = newPageValue;
                }

                //PT: FLOW-466 => Adding threshold info for squaring off articles
                if (ModelSettings.allowSquareOff)
                {
                    //First applying global model-setting for squaringOff threshold
                    _box.squareoffthreshold = ModelSettings.minSpaceForSquaringOff;

                    //Now, lets override squaringOff threshold based on section if any
                    if (ModelSettings.squareOffThresholdOverride.TryGetValue(_box.category, out int squareoffThreshold))
                    {
                        _box.squareoffthreshold = squareoffThreshold;
                    }
                }

                //UM: fixed FLOW-247 => control byline on/off per section
                var boxSectionName = _box.category.ToLower();
                _box.byline = 0;
                //First apply global model-setting for the byline
                if (ModelSettings.hasbyline == 1)
                {
                    BuildByline(ref root, ref _box);
                }
                //Now, lets override section + priority hasbyline if any
                Dictionary<string, int> sectionPriorityMap;
                if (ModelSettings.byLineOverride.TryGetValue(boxSectionName, out sectionPriorityMap))
                {
                    int hasbyline = 0;
                    if (sectionPriorityMap.TryGetValue(_priority, out hasbyline))
                    {
                        if (hasbyline == 0)
                        {
                            _box.byline = 0;
                        }
                        else
                            BuildByline(ref root, ref _box);
                    }
                }
                _box.origArea += _box.byline;

                List<Image> _imagelist = new List<Image>();
                List<Image> _fctlist = new List<Image>();
                List<Image> _citationlist = new List<Image>();

                foreach (string jsonimage in imagesstring)
                {
                    using (JsonDocument _idoc = JsonDocument.Parse(jsonimage))
                    {
                        JsonElement _iroot = _idoc.RootElement;
                        string _storyid = _iroot.GetProperty("SAXo-InternalId").GetString();
                        if (!storyid.Equals(_storyid))
                            continue;

                        string _imagetype = _iroot.GetProperty("imageType").GetString();
                        if (!ModelSettings.SupportedImageTypes.Contains(_imagetype) && !ModelSettings.SupportedContentTypes.Contains(_imagetype) && !ModelSettings.SupportedQuoteTypes.Contains(_imagetype))
                            continue;
                        Image _image = new Image();

                        string _imageuri = _iroot.GetProperty("imageUri").GetString();
                        string _imageid = _iroot.GetProperty("imageUuid").GetString();
                        if (_imageid == "Nfnd")
                            continue;
                        _image.id = _imageid;

                        int _imagepriority = int.Parse(_iroot.GetProperty("ImagePriority").GetString());
                        if (ModelSettings.SupportedImageTypes.Contains(_imagetype))
                        {

                            _image.imagetype = "Image";
                            int _origwidth = 0, _origheight = 0;
                            int.TryParse(_iroot.GetProperty("width").GetString(), out _origwidth);
                            int.TryParse(_iroot.GetProperty("height").GetString(), out _origheight);
                            if (_origwidth <= 0 || _origheight <= 0)
                                continue;
                            JsonElement capelement;
                            if (!_iroot.TryGetProperty("captionLines", out capelement))
                                continue;
                            _image.origwidth = _origwidth;
                            _image.origlength = _origheight;
                            _image.priority = _imagepriority;
                            _imagelist.Add(_image);
                        }
                        else
                        {
                            if (_imageuri.EndsWith("content-part/fact"))
                            {
                                _image.imagetype = "FactBox";
                                _fctlist.Add(_image);
                            }
                            if (_imageuri.EndsWith("content-part/citat"))
                            {
                                _image.imagetype = "Citation";
                                _citationlist.Add(_image);
                            }

                        }



                        if (ModelSettings.SupportedImageTypes.Contains(_imagetype))
                        {
                            JsonElement capelement;
                            if (!_iroot.TryGetProperty("captionLines", out capelement))
                                continue;
                            string imageuid = _imageid + "/" + _storyid; // Appending article id to image id to create a unique id for image caption
                            ImageCaption _imagecaption = new ImageCaption() { imageuid = _imageid + "/" + _storyid };

                            foreach (var _element in capelement.EnumerateArray())
                            {
                                double _ncols;
                                if (!double.TryParse(_element.GetProperty("nCols").ToString(), out _ncols))
                                {
                                    Log.Warning("captionLines: Wrong nCols value recieved in input, skipping.");
                                    continue;
                                }

                                int _lines;
                                if (!int.TryParse(_element.GetProperty("lines").ToString(), out _lines))
                                {
                                    Log.Warning("captionLines: Wrong lines value recieved in input , skipping.");
                                    continue;
                                }

                                int _typelines;
                                if (!int.TryParse(_element.GetProperty("typoLines").ToString(), out _typelines))
                                {
                                    Log.Warning("captionLines: Wrong typoLines value recieved in input , skipping.");
                                    continue;
                                }

                                _imagecaption.addlines(_ncols, _lines, _typelines);
                            }
                            //Ignore the image if ImageCaption doesn't exists

                            if (!lstImageCaption.ContainsKey(imageuid))
                                lstImageCaption.Add(imageuid, _imagecaption);

                            // Handle image MetaData
                            if ((ModelSettings.imagemetadatasupported || ModelSettings.imagemetadatasupportedDT) && _iroot.TryGetProperty("imageProperties", out JsonElement imageProperties))
                            {
                                bool isGraphic = false;
                                foreach (JsonElement property in imageProperties.EnumerateArray())
                                {
                                    if (property.TryGetProperty("graphic", out JsonElement graphic) && graphic.GetString().ToLower() == "true")
                                    {
                                        isGraphic = true;
                                    }
                                    bool isMugShot = property.TryGetProperty("mugshot", out JsonElement mugshot) &&
                                        string.Equals(mugshot.GetString(), "true", StringComparison.OrdinalIgnoreCase);

                                    // Parse imageType details
                                    if (property.TryGetProperty("imageType", out JsonElement imageTypeElement))
                                    {
                                        string imageType = imageTypeElement.GetString();
                                        if (!ParseImageMetadata(imageType, _image, isGraphic, isMugShot))
                                        {
                                            _imagelist.RemoveAll(x => x.id == _image.id);
                                            continue; // This image will be ignored
                                        }
                                    }
                                }
                            }

                            if (ModelSettings.softcropEnabled && _iroot.TryGetProperty("crops", out JsonElement cropsElement))
                            {
                                foreach (JsonElement property in cropsElement.EnumerateArray())
                                {
                                    string cropInfo = property.GetProperty("cropInfo").GetString();
                                    // Parse imageType details
                                    ParseImageSoftCrops(cropInfo, _image);
                                    SetSoftCropImageDimensions(_image);
                                    break;
                                }
                            }
                        }
                        else if (_imageuri.EndsWith("content-part/fact") || _imageuri.EndsWith("content-part/citat"))
                        {
                            string imageuid = _imageid + "/" + _storyid; // Appending article id to image id to create a unique id for image caption
                            if (lstImageCaption.ContainsKey(imageuid))
                            {
                                if (_imageuri.EndsWith("content-part/fact"))
                                    _fctlist.Remove(_image);
                                if (_imageuri.EndsWith("content-part/citat"))
                                    _citationlist.Remove(_image);
                            }
                            else
                            {
                                JsonElement jsonElement = _iroot.GetProperty("factLines");
                                ImageCaption _imagecaption = new ImageCaption() { imageuid = _image.id + "/" + _storyid };
                                foreach (var _element in jsonElement.EnumerateArray())
                                {
                                    int _ncols = int.Parse(_element.GetProperty("nCols").ToString());
                                    int _lines = int.Parse(_element.GetProperty("lines").ToString());
                                    int _typelines = int.Parse(_element.GetProperty("typoLines").ToString());
                                    if (_lines > 0) //EPSLN-145
                                        _imagecaption.addlines(_ncols, _lines, _typelines);
                                }
                                if (_imagecaption.imageCaptionMap != null)
                                {
                                    if (!lstImageCaption.ContainsKey(imageuid))
                                        lstImageCaption.Add(imageuid, _imagecaption);
                                }
                                else
                                {
                                    if (_imageuri.EndsWith("content-part/fact"))
                                        _fctlist.Remove(_image);
                                    if (_imageuri.EndsWith("content-part/citat"))
                                        _citationlist.Remove(_image);
                                }
                            }

                        }
                    }
                }

                /*List<Image> imageimagelist = _imagelist.Where(s => s.imagetype == "Image").ToList().OrderByDescending(x => 100 - x.priority).ToList();

                List<Image> notimagelist = _imagelist.Where(s => s.imagetype != "Image").ToList().OrderByDescending(x => x.imagetype).ToList();
                if (notimagelist.Count() > 0)
                {
                    int _removecount = notimagelist.Count() - ModelSettings.maxFactCitUsed;
                    if (_removecount > 0)
                        notimagelist.RemoveRange(ModelSettings.maxFactCitUsed, _removecount);

                    _removecount = imageimagelist.Count() - (ModelSettings.maxImagesUsed - ModelSettings.maxFactCitUsed);
                    if (_removecount > 0)
                        imageimagelist.RemoveRange(ModelSettings.maxImagesUsed - ModelSettings.maxFactCitUsed, _removecount);
                    imageimagelist.Add(notimagelist[0]);
                }
                else
                {
                    int _removecount = imageimagelist.Count() - ModelSettings.maxImagesUsed;
                    if (_removecount > 0)
                        imageimagelist.RemoveRange(ModelSettings.maxImagesUsed, _removecount);
                }

                for (int _i = 0; _i < imageimagelist.Count(); _i++)
                {
                    imageimagelist[_i].priority = ModelSettings.maxImagesUsed - _i;

                }
                */
                //if (ModelSettings.picturestoriesenabled && sPubName == "MMG_01" && _box.Id == "551ec7f9-1eb7-49b9-8ca7-9daf91e5077e")
                //{
                //    _box.articletype = "picturestories";
                //}
                if (!_box.isdoubletruck && !_box.articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase) && _box.spreadPageCount <= 0)
                {
                    if (_imagelist.Count() > ModelSettings.maxImagesUsed)
                        _imagelist.RemoveRange(ModelSettings.maxImagesUsed, _imagelist.Count() - ModelSettings.maxImagesUsed);
                }
                if (_imagelist.Count() > 0)
                    _imagelist[0].mainImage = 1;
                //if (!_box.isdoubletruck)
                //{
                //    if (_fctlist.Count() > 1)
                //        _fctlist.RemoveRange(1, _fctlist.Count() - 1);
                //}
                if (_citationlist.Count() > 1)
                    _citationlist.RemoveRange(1, _citationlist.Count() - 1);

                BigInteger _imgorder = 1;
                for (int _i = 0; _i < _imagelist.Count(); _i++)
                {
                    _imagelist[_i].priority = _i;
                    _imagelist[_i].imageorderId = _imgorder;
                    _imgorder = _imgorder * 10;
                }

                _box.imageList = _imagelist;
                _box.factList = _fctlist;
                _box.citationList = _citationlist;


                articles.Add(_box);


                JsonElement headlinejson = root.GetProperty("galleyDump").GetProperty("headlines");
                Headline _headline = new Headline();
                _headline.articleid = _box.Id;
                _headline.priority = _ipriority;
                foreach (var _head in headlinejson.EnumerateArray())
                {
                    string _caption = _head.GetProperty("name").GetString();
                    if (_caption.ToUpper() == "SMALL")
                    {
                        foreach (var _hcolmap in _head.GetProperty("columns").EnumerateArray())
                        {
                            int _ncol = int.Parse(_hcolmap.GetProperty("nCols").GetString());
                            int _lines = int.Parse(_hcolmap.GetProperty("lines").GetString()) + ModelSettings.extraheadlineline;
                            _headline.collinemap.Add(_ncol, _lines);
                            int _typolines = int.Parse(_hcolmap.GetProperty("typoLines").GetString());
                            _headline.smalltypomap.Add(_ncol, _typolines);
                        }
                    }
                    if (_caption.ToUpper() == "MEDIUM")
                    {
                        foreach (var _hcolmap in _head.GetProperty("columns").EnumerateArray())
                        {
                            int _ncol = int.Parse(_hcolmap.GetProperty("nCols").GetString());
                            int _lines = int.Parse(_hcolmap.GetProperty("lines").GetString()) + ModelSettings.extraheadlineline;
                            _headline.mediumcollinemap.Add(_ncol, _lines);
                            int _typolines = int.Parse(_hcolmap.GetProperty("typoLines").GetString());
                            _headline.mediumtypomap.Add(_ncol, _typolines);
                        }
                    }
                    if (_caption.ToUpper() == "LARGE")
                    {
                        foreach (var _hcolmap in _head.GetProperty("columns").EnumerateArray())
                        {
                            int _ncol = int.Parse(_hcolmap.GetProperty("nCols").GetString());
                            int _lines = int.Parse(_hcolmap.GetProperty("lines").GetString()) + ModelSettings.extraheadlineline;
                            _headline.largecollinemap.Add(_ncol, _lines);
                            int _typolines = int.Parse(_hcolmap.GetProperty("typoLines").GetString());
                            _headline.largetypomap.Add(_ncol, _typolines);
                        }
                    }
                }
                headlines.Add(_box.Id, _headline);

                if (ModelSettings.haskicker == 1)
                {
                    JsonElement kickerjson = root.GetProperty("galleyDump").GetProperty("kickers");

                    foreach (var _kick in kickerjson.EnumerateArray())
                    {
                        string _kicker = _kick.GetProperty("name").GetString();
                        if (_kicker == "kicker")
                        {
                            Kicker kickerobj = new Kicker();
                            kickerobj.articleid = _box.Id;
                            kickerobj.priority = _ipriority;
                            foreach (var _colmap in _kick.GetProperty("columns").EnumerateArray())
                            {
                                int _ncol = int.Parse(_colmap.GetProperty("nCols").GetString());
                                int _lines = int.Parse(_colmap.GetProperty("lines").GetString());
                                kickerobj.collinemap.Add(_ncol, _lines);
                            }
                            kickersmap.Add(_box.Id, kickerobj);
                        }
                    }
                }
                foreach (var _element in root.GetProperty("specialElements").EnumerateArray())
                {
                    if (_element.GetProperty("elementType").GetString().Equals("headline"))
                    {
                        headlineMap.Add(storyid, _element.GetProperty("elementText").GetString());
                    }
                }//.GetProperty("headlines");
            }
        }


        foreach (string _section in lstPageSection)
        {
            System.Numerics.BigInteger i = 1;
            List<Box> _temp = articles.Where(x => x.category == _section).OrderByDescending(y => y.priority).ThenByDescending(z => z.length * z.width).ToList();
            foreach (var _box in _temp)
            {
                _box.boxorderId = i;
                i = i * 10;
            }
        }


        LoadRelatedArticles();
        if (ModelSettings.hasloftarticle)
        {
            foreach (var item in articles.Where(x => x.priority == 3 && (x.articletype == "" || x.articletype.ToLower() == "nfnd")))
            {
                item.articletype = "loft";
            }
        }

    }

    private void LoadRelatedArticles()
    {
        string storiesjson = sJsonDirectory + srunName + "Stories.json";
        string[] storiesstring = System.IO.File.ReadAllLines(storiesjson);

        foreach (string jsonstory in storiesstring)
        {
            using (JsonDocument document = JsonDocument.Parse(jsonstory))
            {
                JsonElement root = document.RootElement;
                string storyid = root.GetProperty("SAXo-InternalId").GetString();

                JsonElement relatedArticlesElement;
                if (root.TryGetProperty("relatedArticles", out relatedArticlesElement))
                {
                    foreach (var _element in relatedArticlesElement.EnumerateArray())
                    {
                        string relateduid = _element.GetProperty("relatedUuid").ToString();
                        bool relatedflag = bool.Parse(_element.GetProperty("relatedFlag").ToString());
                        if (articles.Exists(x => x.Id == relateduid))
                        {
                            Box _article = articles.Where(x => x.Id == relateduid).ToList()[0];
                            _article.parentArticleId = storyid;
                        }

                    }
                }

            }
        }
        //"relatedArticles"
    }

    private bool IsPatternInSection(string input, string pattern, List<string> excludeList)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        Regex regex = new Regex(pattern);

        // Check if the input matches the pattern and is not in the exclude list
        return regex.IsMatch(input) && (excludeList == null || !excludeList.Contains(input));
    }

    private void RemapSection(PageInfo page)
    {
        var sectionMap = ModelSettings.sectionMappingList;

        var matchedSection = sectionMap.FirstOrDefault(val =>
            (val.section != null && val.section.Equals(page.section, StringComparison.OrdinalIgnoreCase)) ||
            (val.hasPatternDefined && val.pattern != null && IsPatternInSection(page.section, val.pattern, val.exclude))
        );

        if (matchedSection != null)
        {
            Log.Information("RemapSection: section {section} mapped to {targetSection} or pageId {pageid}", page.section, matchedSection.targetSection, page.pageid);
            page.section = matchedSection.targetSection;
        }
    }


    private void LoadAds()
    {
        string dialsjson = sJsonDirectory + srunName + "DailyDials.json";
        string adsjson = sJsonDirectory + srunName + "adstack.json";

        string[] diastring = System.IO.File.ReadAllLines(dialsjson);
        HashSet<String> ignorepageSet = new HashSet<string>();
        using (JsonDocument document = JsonDocument.Parse(diastring[0]))
        {
            foreach (var _docjson in document.RootElement.EnumerateArray())
            {
                if (_docjson.GetProperty("dial").ToString() == "dontUsePages")
                {

                    foreach (var _arr in _docjson.GetProperty("Arr").EnumerateArray())
                    {
                        if (!ignorepageSet.Contains(_arr.GetString()))
                            ignorepageSet.Add(_arr.GetString());
                    }
                }

            }
        }

        string[] adsstring = System.IO.File.ReadAllLines(adsjson);
        int _pageorder = 0;
        foreach (string jsonads in adsstring)
        {
            _pageorder++;
            using (JsonDocument document = JsonDocument.Parse(jsonads))
            {
                JsonElement root = document.RootElement;
                JsonElement pageinfoelement = root.GetProperty("pageInfo");
                int _pageid = int.Parse(pageinfoelement.GetProperty("Calc:Page").GetString());
                string _np = pageinfoelement.GetProperty("NEWSPAPER").GetString();
                string _section = pageinfoelement.GetProperty("Calc:SAXo_Category").GetString().Trim();

                string _sname = pageinfoelement.GetProperty("SNAME").GetString();
                double _height = pageinfoelement.GetProperty("HEIGHT").GetDouble();
                PageInfo _pinfo = new PageInfo()
                {
                    pageid = _pageid,
                    newspaper = _np,
                    section = _section,
                    sname = _sname,
                    height = _height,
                    sectionheaderheight = ModelSettings.sectionheadheight,
                    pageorder = _pageorder,
                    pageEditorialArea = new StaticBox(0, 0, 0, 0, FlowElements.Editorial)
                };

                if (ignorepageSet.Contains(_sname + _pageid.ToString()))
                    _pinfo.ignorepage = true;
                JsonElement adroot = root.GetProperty("ads");
                foreach (var _adelement in adroot.EnumerateArray())
                {
                    int _class = int.Parse(_adelement.GetProperty("Class").GetString());
                    string _fname = _adelement.GetProperty("File").GetString();
                    string _type = _adelement.GetProperty("Type").GetString();
                    double _x = double.Parse(_adelement.GetProperty("X").GetString());
                    double _y = double.Parse(_adelement.GetProperty("Y").GetString());
                    double _dx = double.Parse(_adelement.GetProperty("dX").GetString());
                    double _dy = double.Parse(_adelement.GetProperty("dY").GetString());

                    _x = _x / ModelSettings.columnWidth;
                    _y = _y / ModelSettings.lineHeight;
                    _dx = _dx / ModelSettings.columnWidth;
                    _dy = _dy / ModelSettings.lineHeight;
                    int _newx = (int)Math.Floor(_x);
                    int _newy = (int)(Math.Floor(_y));
                    int _adwidth = (int)(Math.Round(_dx));
                    int _adheight = (int)Math.Ceiling(_dy);

                    PageAds _pa = new PageAds() { adclass = _class, filename = _fname, type = _type, x = _x, y = _y, dx = _dx, dy = _dy };

                    //POK: Ignore the ad if the width or height comes out to be 0
                    if (_adwidth == 0 || _adheight == 0)
                        continue;

                    if (ModelSettings.bExtendAdToTheBottom && (_newy + _adheight < ModelSettings.canvasheight) && (_newy + _adheight >= ModelSettings.canvasheight - 10))
                        _adheight += ModelSettings.canvasheight - (_newy + _adheight);
                    if (_newx + _adwidth > canvasx)
                        _adwidth = canvasx - _newx;
                    _pa.newx = _newx < 0 ? 0 : _newx;
                    _pa.newy = _newy;
                    _pa.newwidth = _adwidth;
                    _pa.newheight = _adheight;
                    _pinfo.ads.Add(_pa);
                }

                RemapSection(_pinfo); //Umesh : FLOW-291 : Section remapping

                if (!lstPageSection.Contains(_pinfo.section))
                    lstPageSection.Add(_pinfo.section);

                lstPages.Add(_pinfo);
            }
            if (ModelSettings.quarterpageAdEnabled)
                foreach (var _page in lstPages.Where(x => x.ads.Count == 1))
                {
                    if (_page.ads[0].newwidth == 3 && _page.ads[0].newheight >= 50 && _page.ads[0].newy >= 49)
                        _page.hasQuarterPageAds = true;
                }
        }

        foreach (var _customsectionheader in ModelSettings.clsSectionheader.defaultSectionHeaderheight)
        {
            string _section = _customsectionheader.Key;
            int _height = _customsectionheader.Value;

            foreach (var item in lstPages.Where(x => x.section.ToLower() == _section.ToLower()))
            {
                item.sectionheaderheight = _height;
            }
        }

        foreach (var _largesh in ModelSettings.clsSectionheader.firstlargeSectionHeader)
        {
            string _section = _largesh.Key;
            int _height = _largesh.Value;
            List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());

            if (_lstinfo.Count > 0)
            {
                PageInfo _firstpage = _lstinfo[0];
                _firstpage.sectionheaderheight = _height;
                _firstpage.haslargesectionheader = true;
            }
        }

        foreach (var _largesh in ModelSettings.clsSectionheader.firsttwolargeSectionHeader)
        {
            string _section = _largesh.Key;
            int _height = _largesh.Value;
            List<PageInfo> _lstinfo = lstPages.FindAll(x => x.section.ToLower() == _section.ToLower());
            if (_lstinfo.Count > 0)
            {
                _lstinfo[0].sectionheaderheight = _height;
                _lstinfo[0].haslargesectionheader = true;
            }
            if (_lstinfo.Count > 1 && _lstinfo[0].pageid % 2 == 0 && _lstinfo[0].pageid + 1 == _lstinfo[1].pageid)
            {
                _lstinfo[1].sectionheaderheight = _height;
                _lstinfo[1].haslargesectionheader = true;
            }

        }

        foreach (var _largesh in ModelSettings.clsSectionheader.lastlargeSectionHeader)
        {
            string _section = _largesh.Key;
            int _height = _largesh.Value;
            PageInfo _lastpage = lstPages.Last(x => x.section.ToLower() == _section.ToLower());
            if (_lastpage != null)
            {
                _lastpage.sectionheaderheight = _height;
                _lastpage.haslargesectionheader = true;
            }
        }

        //discard sectionheaders on pages with overlapping ads
        if (ModelSettings.disableSectionHeaderIfOverlapsAd && ModelSettings.hassectionheader == 1)
        {
            foreach (var page in lstPages)
            {
                if (page.ads.Any(ad => ad.newy < page.sectionheaderheight))
                {
                    page.sectionheaderheight = 0;
                }
            }
        }
    }
    private async Task<string?> LoadModelSettingsFromS3(string pubName, AppSettings settings)
    {
        string orgName = pubName.Split('_')[0];
        Log.Information("Model tuning for {orgName}'s publication {pubName} is being loaded from S3", orgName, pubName);

        if (settings.ModelTuningLocationsS3 == null)
        {
            Log.Warning("{orgName}'s configuration is missing from appsetting.JSON", orgName);
            return null;
        }

        try
        {
            string mtFilePath = await GetMTPathFromOrgConfig(orgName, pubName, settings);
            if (string.IsNullOrEmpty(mtFilePath))
                return null;

            string mtContent = await LoadMTFileFromS3(orgName, mtFilePath, settings);

            return mtContent;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load MT file from S3 for {PubName}: {Message}", pubName, ex.Message);
            return null;
        }
    }

    private async Task<string?> GetMTPathFromOrgConfig(string orgName, string pubName, AppSettings settings)
    {
        // Handle BucketName
        string? bucketName;
        if (!settings.ModelTuningLocationsS3.TryGetValue("BucketName", out bucketName) ||
            string.IsNullOrWhiteSpace(bucketName))
        {
            Log.Error("BucketName is missing or empty in ModelTuningLocationsS3.");
        }

        // Handle Key
        string? appKey;
        if (!settings.ModelTuningLocationsS3.TryGetValue("Key", out appKey) ||
            string.IsNullOrWhiteSpace(appKey))
        {
            Log.Error("Appsetting key is missing or empty in ModelTuningLocationsS3.");
        }

        var orgConfig = new Dictionary<string, string>();

        try
        {
            RegionEndpoint regionEndpoint = null;

            if (settings.ModelTuningLocationsS3.TryGetValue("Region", out var regionValue) &&
                    !string.IsNullOrWhiteSpace(regionValue))
            {
                regionEndpoint = RegionEndpoint.GetBySystemName(regionValue);
            }

            AmazonS3Client s3Client = regionEndpoint != null
               ? new AmazonS3Client(regionEndpoint)
               : new AmazonS3Client();

            var listrequest = new ListObjectsRequest
            {
                BucketName = bucketName,
                Prefix = appKey
            };

            GetObjectRequest request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = appKey
            };

            using GetObjectResponse response = await s3Client.GetObjectAsync(request);
            using StreamReader reader = new StreamReader(response.ResponseStream);
            string configContent = await reader.ReadToEndAsync();

            using JsonDocument configDoc = JsonDocument.Parse(configContent);

            foreach (var prop in configDoc.RootElement.EnumerateObject())
            {
                orgConfig[prop.Name] = prop.Value.GetString();
            }
        }
        catch (Exception e)
        {
            Log.Error("Failed to load appsettings from S3 for {orgName}: {Message}", orgName, e.Message);
        }

        return orgConfig[pubName];
    }

    private async Task<string> LoadMTFileFromS3(string orgName, string mtFilePath, AppSettings settings)
    {
        RegionEndpoint regionEndpoint = null;

        if (settings.ModelTuningLocationsS3.TryGetValue("Region", out var regionValue) &&
                !string.IsNullOrWhiteSpace(regionValue))
        {
            regionEndpoint = RegionEndpoint.GetBySystemName(regionValue);
        }


        AmazonS3Client s3Client = regionEndpoint != null
            ? new AmazonS3Client(regionEndpoint)
            : new AmazonS3Client();

        string? bucketName;
        if (!settings.ModelTuningLocationsS3.TryGetValue("BucketName", out bucketName) ||
            string.IsNullOrWhiteSpace(bucketName))
        {
            Log.Error("BucketName is missing or empty in ModelTuningLocationsS3.");
        }

        GetObjectRequest request = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = mtFilePath
        };

        using GetObjectResponse response = await s3Client.GetObjectAsync(request);
        using StreamReader reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync();
    }

    private void LoadModelSettings(string? modelSettingCLI, AppSettings? settings, string? pubName)
    {
        string modelstring = TryLoadModelTuningContent(modelSettingCLI, settings, pubName);
        if (string.IsNullOrEmpty(modelstring))
            return;

        using (JsonDocument document = JsonDocument.Parse(modelstring))
        {
            JsonElement root = document.RootElement;
            JsonElement pageElement = root.GetProperty("page");
            //JsonElement footerElement = root.GetProperty("sectionfooter");

            JsonElement articlesElement = root.GetProperty("articles");
            JsonElement truckElement = root.GetProperty("doubletruck");

            canvasx = int.Parse(pageElement.GetProperty("canvaswidth").ToString());
            canvasz = int.Parse(pageElement.GetProperty("canvasheight").ToString());


            if (pageElement.TryGetProperty("customplacementrules", out var customplacementrulesElement))
                ModelSettings.bCustomPlacementEnabled = customplacementrulesElement.GetBoolean();

            if (pageElement.TryGetProperty("sidebarWidth", out var sidebarWidthElement))
                ModelSettings.sidebarWidth = sidebarWidthElement.GetInt32();


            if (pageElement.TryGetProperty("enableArticleJumps", out var enableArticleJumpsElement))
                ModelSettings.enableArticleJumps = enableArticleJumpsElement.GetBoolean();

            if (pageElement.TryGetProperty("SupportedImageTypes", out var SupportedImageTypes))
            {
                var imageTypesToAdd = JsonSerializer.Deserialize<List<string>>(pageElement.GetProperty("SupportedImageTypes"));
                ModelSettings.SupportedImageTypes.AddRange(imageTypesToAdd);
            }
            if (pageElement.TryGetProperty("SupportedQuoteTypes", out var SupportedQuoteTypes))
            {
                var quoteTypesToAdd = JsonSerializer.Deserialize<List<string>>(pageElement.GetProperty("SupportedQuoteTypes"));
                ModelSettings.SupportedQuoteTypes.AddRange(quoteTypesToAdd);
            }
            if (pageElement.TryGetProperty("SupportedContentTypes", out var SupportedContentTypes))
            {
                var contentTypesToAdd = JsonSerializer.Deserialize<List<string>>(pageElement.GetProperty("SupportedContentTypes"));
                ModelSettings.SupportedContentTypes.AddRange(contentTypesToAdd);
            }

            if (pageElement.TryGetProperty("placeImageAtCenter", out var placeImageAtCenterElement))
                ModelSettings.placeImageAtCenter = placeImageAtCenterElement.GetBoolean();

            if (ModelSettings.enableArticleJumps)
            {
                if (root.TryGetProperty("jumps", out var jumpElement))
                {
                    ModelSettings.jumpArticleSettings = jumpElement.Deserialize<ModelSettings.JumpSettings>();
                }
            }
            if (ModelSettings.jumpArticleSettings.jumpSections.Count <= 0)
                ModelSettings.enableArticleJumps = false;

            if (pageElement.TryGetProperty("textWrapEnabled", out var textWrapEnabledElement))
                ModelSettings.bTextWrapEnabled = textWrapEnabledElement.GetBoolean();

            if (pageElement.TryGetProperty("softcropEnabled", out var softcropEnabledElement))
                ModelSettings.softcropEnabled = softcropEnabledElement.GetBoolean();


            if (pageElement.TryGetProperty("enableOversetArticles", out var enableOversetArticlesElement))
                ModelSettings.enableTextOverset = enableOversetArticlesElement.GetBoolean();

            if (pageElement.TryGetProperty("extendadtobottom", out var extendadtobottomElement))
                ModelSettings.bExtendAdToTheBottom = extendadtobottomElement.GetBoolean();

            if (pageElement.TryGetProperty("maxPermutationsPerPageInMillions", out var maxPermutationsPerPageInMillionsElement))
            {
                double maxpermutationvalue = maxPermutationsPerPageInMillionsElement.GetDouble();
                if (maxpermutationvalue <= 50 && maxpermutationvalue > 0)
                    ModelSettings.MaxPermutationsPerPage = (int)(maxpermutationvalue * 1000000);
            }
            if (ModelSettings.enableTextOverset)
            {
                if (root.TryGetProperty("overset", out var oversetElement))
                {
                    ModelSettings.oversetRules = oversetElement.Deserialize<ModelSettings.OversetRules>();
                }
            }

            JsonElement mandatoryElement;

            if (pageElement.TryGetProperty("disableSectionHeaderIfOverlapsAd", out var _))
                ModelSettings.disableSectionHeaderIfOverlapsAd = bool.Parse(pageElement.GetProperty("disableSectionHeaderIfOverlapsAd").ToString());

            if (pageElement.TryGetProperty("enableNewLayoutsBelowHeadline", out var newLayoutProp))
                ModelSettings.enableNewLayoutsBelowHeadline = bool.Parse(newLayoutProp.ToString());

            if (ModelSettings.enableNewLayoutsBelowHeadline)
            {
                ModelSettings.belowHeadlineLayoutSettings = root.TryGetProperty("belowHeadlineLayoutSettings", out var belowHeadlineSettings) ?
                    belowHeadlineSettings.Deserialize<BelowHeadlineLayoutSettings>() : new BelowHeadlineLayoutSettings();
            }

            if (pageElement.TryGetProperty("LayoutPreferenceOrder", out var layoutPreference))
                ModelSettings.LayoutPreferenceOrder = layoutPreference.Deserialize<List<string>>();

            if (pageElement.TryGetProperty("generatePngFiles", out var pngProp))
                ModelSettings.generatePngFiles = bool.Parse(pngProp.ToString());

            if (pageElement.TryGetProperty("picturestoriesdtenabled", out var picstoryProp))
                ModelSettings.picturestoriesdtenabled = bool.Parse(picstoryProp.ToString());

            if (pageElement.TryGetProperty("mandatorylistOrder", out mandatoryElement))
                ModelSettings.mandatoryListOrder = int.Parse(pageElement.GetProperty("mandatorylistOrder").ToString());
            else
                ModelSettings.mandatoryListOrder = 0;

            JsonElement newPageEnabled;
            if (pageElement.TryGetProperty("newPageEnabled", out newPageEnabled))
                ModelSettings.newPageEnabled = bool.Parse(pageElement.GetProperty("newPageEnabled").ToString());
            else
                ModelSettings.newPageEnabled = false;

            JsonElement assignNewPageToAArtcile;
            if (pageElement.TryGetProperty("addNewpageToAArticle", out assignNewPageToAArtcile))
                ModelSettings.enableNewPageToAArticle = bool.Parse(pageElement.GetProperty("addNewpageToAArticle").ToString());
            else
                ModelSettings.enableNewPageToAArticle = true;

            if (pageElement.TryGetProperty("hasloftarticle", out var loftarticleElement))
                ModelSettings.hasloftarticle = bool.Parse(loftarticleElement.ToString());
            else
                ModelSettings.hasloftarticle = false;

            // to enable placement of facts at The bottom of article
            if (pageElement.TryGetProperty("enablefactsatbottom", out var enablefactsatbottom))
                ModelSettings.enablefactsatbottom = bool.Parse(pageElement.GetProperty("enablefactsatbottom").ToString());
            else
                ModelSettings.enablefactsatbottom = false;

            if (pageElement.TryGetProperty("samesizesubimageallowed", out var samesizesubimageallowed))
                ModelSettings.samesizesubimageallowed = bool.Parse(pageElement.GetProperty("samesizesubimageallowed").ToString());
            else
                ModelSettings.samesizesubimageallowed = false;

            if (pageElement.TryGetProperty("morelayoutsformultifacts", out var morelayoutsformultifacts))
                ModelSettings.morelayoutsformultifacts = bool.Parse(pageElement.GetProperty("morelayoutsformultifacts").ToString());
            else
                ModelSettings.morelayoutsformultifacts = false;

            if (ModelSettings.morelayoutsformultifacts)
            {
                if (pageElement.TryGetProperty("multiFactStackingOrderPreference", out var stackingOrderPreferences))
                    ModelSettings.multiFactStackingOrderPreference = stackingOrderPreferences.Deserialize<List<string>>();
            }

            if (pageElement.TryGetProperty("allowSquareOff", out var allowSquareOff))
                ModelSettings.allowSquareOff = bool.Parse(pageElement.GetProperty("allowSquareOff").ToString());
            else
                ModelSettings.allowSquareOff = false;

            if (ModelSettings.allowSquareOff)
            {
                if (root.TryGetProperty("squareOffThreshold", out var squareOffThresholdElement))
                {
                    if (squareOffThresholdElement.TryGetProperty("minSpaceForSquaringOff", out var minSpaceForSquaringOff))
                        ModelSettings.minSpaceForSquaringOff = int.Parse(minSpaceForSquaringOff.ToString());
                    else
                        ModelSettings.minSpaceForSquaringOff = 0;

                    if (squareOffThresholdElement.TryGetProperty("thresholdOverride", out var thresholdOverrideElement))
                    {
                        foreach (var thresholdelement in thresholdOverrideElement.EnumerateArray())
                        {
                            int thresholdvalue = int.Parse(thresholdelement.GetProperty("thresholdvalue").ToString());

                            foreach (var section in thresholdelement.GetProperty("section").EnumerateArray())
                            {
                                if (!ModelSettings.squareOffThresholdOverride.ContainsKey(section.ToString()))
                                    ModelSettings.squareOffThresholdOverride.Add(section.ToString(), thresholdvalue);
                            }
                        }
                    }
                }
            }

            if (root.TryGetProperty("whitespaceSettings", out var whitespaceElement))
            {
                ModelSettings.clsWhiteSpaceSettings.addwhitespacelines = bool.Parse(whitespaceElement.GetProperty("addwhitespacelines").ToString());
                ModelSettings.clsWhiteSpaceSettings.minwhitespacelines = int.Parse(whitespaceElement.GetProperty("minwhitespacelines").ToString());
                ModelSettings.clsWhiteSpaceSettings.maxwhitespacelines = int.Parse(whitespaceElement.GetProperty("maxwhitespacelines").ToString());

            }
            else
                ModelSettings.clsWhiteSpaceSettings.addwhitespacelines = false;

            ModelSettings.canvaswidth = int.Parse(pageElement.GetProperty("canvaswidth").ToString());
            ModelSettings.canvasheight = int.Parse(pageElement.GetProperty("canvasheight").ToString());
            ModelSettings.columnWidth = double.Parse(pageElement.GetProperty("columnWidth").ToString());
            ModelSettings.gutterWidth = double.Parse(pageElement.GetProperty("gutterWidth").ToString());
            ModelSettings.lineHeight = double.Parse(pageElement.GetProperty("lineHeight").ToString());
            ModelSettings.hassectionfooter = int.Parse(pageElement.GetProperty("hassectionfooter").ToString());
            ModelSettings.haskicker = int.Parse(pageElement.GetProperty("haskicker").ToString());
            ModelSettings.hasbyline = int.Parse(pageElement.GetProperty("hasbyline").ToString());
            ModelSettings.extraimagecaptionline = int.Parse(pageElement.GetProperty("extraimagecaptionline").ToString());
            ModelSettings.extralineForByline = int.Parse(pageElement.GetProperty("extralineForByline").ToString());

            if (pageElement.TryGetProperty("AllowedStaticBoxSpacingY", out var allowedStaticBoxSpacingY))
                ModelSettings.allowedStaticBoxSpacingY = int.Parse(allowedStaticBoxSpacingY.ToString());

            if (pageElement.TryGetProperty("extralineForPreamble", out var extralineForPreambleElement))
            {
                ModelSettings.extralineforPreamble = int.Parse(extralineForPreambleElement.ToString());
            }

            //ModelSettings.ignorepagelist = pageElement.GetProperty("ignorepagelist").ToString();
            ModelSettings.articleseparatorheight = int.Parse(pageElement.GetProperty("articleseparatorheight").ToString());
            ModelSettings.sectionheadheight = int.Parse(pageElement.GetProperty("sectionheadheight").ToString());
            ModelSettings.hassectionheader = int.Parse(pageElement.GetProperty("hassectionheader").ToString());
            ModelSettings.maxImagesUsed = int.Parse(pageElement.GetProperty("maxImagesForArticles").ToString());
            ModelSettings.maxFactCitUsed = int.Parse(pageElement.GetProperty("maxFactCitationsForArticles").ToString());
            ModelSettings.highPriorityArticleStart = int.Parse(pageElement.GetProperty("highPriorityArticleStartRange").ToString());
            ModelSettings.bCropAllowed = int.Parse(pageElement.GetProperty("cropAllowed").ToString()) == 1 ? true : false;
            ModelSettings.croppercentage = double.Parse(pageElement.GetProperty("cropPercentage").ToString());

            if (pageElement.TryGetProperty("minPageAreaPercentage", out var minPageAreaPercentage))
                ModelSettings.minPageAreaPercentageThreshold = double.Parse(minPageAreaPercentage.ToString());
            else
                ModelSettings.minPageAreaPercentageThreshold = 5;

            //ModelSettings.sectionfooterX = int.Parse(footerElement.GetProperty("sectionfooterx").ToString());
            //ModelSettings.sectionfooterY = int.Parse(footerElement.GetProperty("sectionfootery").ToString());
            //ModelSettings.sectionfooterwidth = int.Parse(footerElement.GetProperty("sectionfooterwidth").ToString());
            //ModelSettings.sectionfooterlength = int.Parse(footerElement.GetProperty("sectionfooterheight").ToString());
            //string sectionfootersections = footerElement.GetProperty("sectionfootersections").ToString();
            //ModelSettings.sectionfootersections = sectionfootersections.Split(',');
            ModelSettings.extraheadlineline = int.Parse(pageElement.GetProperty("extraheadlineline").ToString());
            ModelSettings.minSpaceBetweenAdsAndStories = int.Parse(pageElement.GetProperty("minSpaceBetweenAdsAndStories").ToString());
            if (pageElement.TryGetProperty("minSpaceBetweenFooterAndStories", out var minSpace))
                ModelSettings.minSpaceBetweenFooterAndStories = int.Parse(minSpace.ToString());
            ModelSettings.extrafactline = int.Parse(pageElement.GetProperty("extrafactline").ToString());
            ModelSettings.extraquoteline = int.Parse(pageElement.GetProperty("extraquoteline").ToString());
            ModelSettings.multiColumnFactMinLines = int.Parse(pageElement.GetProperty("multiColumnFactMinLines").ToString());
            JsonElement _tempelement;
            if (pageElement.TryGetProperty("minimumlinesunderImage", out _tempelement))
                ModelSettings.minimumlinesunderImage = int.Parse(pageElement.GetProperty("minimumlinesunderImage").ToString());
            else
                ModelSettings.minimumlinesunderImage = 3;

            if (pageElement.TryGetProperty("enablePlacementFromTopRight", out var placementFromTopRightElement))
            {
                ModelSettings.enablePlacementFromTopRight = bool.Parse(placementFromTopRightElement.ToString());
            }

            if (pageElement.TryGetProperty("picturestories", out var picturestoriesElement))
            {
                ModelSettings.picturestoriesenabled = bool.Parse(picturestoriesElement.ToString());
            }

            if (pageElement.TryGetProperty("imagemetadatasupported", out var metadataSupported))
            {
                ModelSettings.imagemetadatasupported = bool.Parse(metadataSupported.ToString());
            }

            if (pageElement.TryGetProperty("imagemetadatasupportedDT", out var metadataSupportedDT))
            {
                ModelSettings.imagemetadatasupportedDT = bool.Parse(metadataSupportedDT.ToString());
            }

            if (pageElement.TryGetProperty("enableDTGraphicalSubImages", out var graphicsSubimageSpportedDT))
            {
                ModelSettings.enableDTGraphicalSubImages = bool.Parse(graphicsSubimageSpportedDT.ToString());
            }
            if (pageElement.TryGetProperty("enableNewAlgorithmForPictureStory", out var enableNewAlgorithmForPictureStory))
            {
                ModelSettings.enableNewAlgorithmForPictureStory = bool.Parse(enableNewAlgorithmForPictureStory.ToString());
            }
            if (pageElement.TryGetProperty("placelowerpriorityarticleatleftorright", out var placelowerpriorityarticleatleftorrightElement))
            {
                ModelSettings.placelowerpriorityarticleatleftorright = bool.Parse(placelowerpriorityarticleatleftorrightElement.ToString());
            }

            if (root.TryGetProperty("sectionheader", out var headerElement))
            {
                if (headerElement.TryGetProperty("default", out var defaultheaderElement))
                {
                    foreach (var _defaultheader in defaultheaderElement.EnumerateArray())
                    {
                        int _headerheight = int.Parse(_defaultheader.GetProperty("height").ToString());
                        foreach (var _section in _defaultheader.GetProperty("sections").EnumerateArray())
                        {
                            if (!ModelSettings.clsSectionheader.defaultSectionHeaderheight.ContainsKey(_section.ToString()))
                                ModelSettings.clsSectionheader.defaultSectionHeaderheight.Add(_section.ToString(), _headerheight);
                        }
                    }
                }

                if (headerElement.TryGetProperty("large", out var largeheaderElement))
                {
                    foreach (var _largeheader in largeheaderElement.EnumerateArray())
                    {
                        int _headerheight = int.Parse(_largeheader.GetProperty("height").ToString());
                        string _location = _largeheader.GetProperty("location").ToString().ToLower();
                        foreach (var _section in _largeheader.GetProperty("sections").EnumerateArray())
                        {
                            if (_location == "first")
                            {
                                if (!ModelSettings.clsSectionheader.firstlargeSectionHeader.ContainsKey(_section.ToString()))
                                    ModelSettings.clsSectionheader.firstlargeSectionHeader.Add(_section.ToString(), _headerheight);
                            }
                            else if (_location == "firsttwo")
                            {
                                if (!ModelSettings.clsSectionheader.firsttwolargeSectionHeader.ContainsKey(_section.ToString()))
                                    ModelSettings.clsSectionheader.firsttwolargeSectionHeader.Add(_section.ToString(), _headerheight);
                            }
                            else if (_location == "last")
                            {
                                if (!ModelSettings.clsSectionheader.lastlargeSectionHeader.ContainsKey(_section.ToString()))
                                    ModelSettings.clsSectionheader.lastlargeSectionHeader.Add(_section.ToString(), _headerheight);
                            }

                        }
                    }
                }
            }

            if (ModelSettings.enablefactsatbottom)
            {
                if (root.TryGetProperty("fullwidthfactsatbottom", out var fullwidthfacts))
                {
                    foreach (var factsizeelement in fullwidthfacts.EnumerateArray())
                    {
                        FullWidthFactSettings fullWidthFactSettings = new FullWidthFactSettings();
                        int _priority = Helper.GetNumericalPriority(factsizeelement.GetProperty("priority").ToString());
                        string[] _sfctvals = factsizeelement.GetProperty("columnsizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        int[] _ifctvals = new int[_sfctvals.Length];
                        for (int _i = 0; _i < _sfctvals.Length; _i++)
                        {
                            _ifctvals[_i] = int.Parse(_sfctvals[_i].Trim());
                        }
                        HashSet<int> _facthashset = _ifctvals.ToHashSet().OrderBy(x => x).ToHashSet();
                        fullWidthFactSettings.factSizes = _facthashset;
                        fullWidthFactSettings.minHeight = int.Parse(factsizeelement.GetProperty("minheight").ToString());
                        ModelSettings.fullwidthfactsettings.Add(_priority, fullWidthFactSettings);
                    }
                }
            }

            if (root.TryGetProperty("multiColumnFactMinHeights", out var multiColumnFactMinHeightElement))
            {
                foreach (var FactMinHeightElement in multiColumnFactMinHeightElement.EnumerateArray())
                {
                    int _width = FactMinHeightElement.GetProperty("column").GetInt32();
                    int _minheight = FactMinHeightElement.GetProperty("lines").GetInt32();
                    ModelSettings.multiColumnFactMinHeights[_width] = _minheight;    
                }
            }

            if (ModelSettings.hassectionfooter == 1)
            {
                //for (int _i = 0; _i < ModelSettings.sectionfootersections.Length; _i++)
                //{
                //    ModelSettings.sectionfootersections[_i] = ModelSettings.sectionfootersections[_i].Trim();
                //}
                if (root.TryGetProperty("sectionfooter", out var footerElement))
                {
                    if (footerElement.TryGetProperty("default", out var defaultfooterElement))
                    {
                        foreach (var _defaultfooter in defaultfooterElement.EnumerateArray())
                        {
                            int _footerx = int.Parse(_defaultfooter.GetProperty("footerx").ToString());
                            int _footery = int.Parse(_defaultfooter.GetProperty("footery").ToString());
                            int _footerwidth = int.Parse(_defaultfooter.GetProperty("footerwidth").ToString());
                            int _footerheight = int.Parse(_defaultfooter.GetProperty("footerheight").ToString());
                            string _footerloc = _defaultfooter.GetProperty("location").ToString().ToLower();
                            foreach (var _section in _defaultfooter.GetProperty("sections").EnumerateArray())
                            {
                                SectionFooter _sectionfooter = new SectionFooter() { x = _footerx, y = _footery, width = _footerwidth, height = _footerheight };
                                if (_footerloc == "first")
                                {
                                    if (!ModelSettings.clsSectionFooter.firstSectionFooter.ContainsKey(_section.ToString()))
                                        ModelSettings.clsSectionFooter.firstSectionFooter.Add(_section.ToString(), _sectionfooter);
                                }
                                if (_footerloc == "firsttwo")
                                {
                                    if (!ModelSettings.clsSectionFooter.firsttwoSectionFooter.ContainsKey(_section.ToString()))
                                        ModelSettings.clsSectionFooter.firsttwoSectionFooter.Add(_section.ToString(), _sectionfooter);
                                }
                                if (_footerloc == "last")
                                {
                                    if (!ModelSettings.clsSectionFooter.lastSectionFooter.ContainsKey(_section.ToString()))
                                        ModelSettings.clsSectionFooter.lastSectionFooter.Add(_section.ToString(), _sectionfooter);
                                }

                                if (_footerloc == "firstleft") // New Location: firstleft
                                {
                                    if (!ModelSettings.clsSectionFooter.firstLeftSectionFooter.ContainsKey(_section.ToString()))
                                        ModelSettings.clsSectionFooter.firstLeftSectionFooter.Add(_section.ToString(), _sectionfooter);
                                }

                                if (_footerloc.All(char.IsDigit) || _footerloc.Contains(",")) // Single or Multiple Page Numbers
                                {
                                    var pageNumbers = _footerloc.Split(',')
                                                                .Select(s => int.TryParse(s.Trim(), out int num) ? num : (int?)null)
                                                                .Where(n => n.HasValue)
                                                                .Select(n => n.Value)
                                                                .ToList();

                                    if (pageNumbers.Count > 0)
                                    {
                                        if (!ModelSettings.clsSectionFooter.customPageSectionFooter.ContainsKey(_section.ToString()))
                                        {
                                            ModelSettings.clsSectionFooter.customPageSectionFooter[_section.ToString()] = new CustomPageSectionFooter();
                                        }

                                        ModelSettings.clsSectionFooter.customPageSectionFooter[_section.ToString()].PageNumbers = pageNumbers;
                                        ModelSettings.clsSectionFooter.customPageSectionFooter[_section.ToString()].Footer = _sectionfooter;
                                    }
                                }
                            }
                        }
                    }
                    if (footerElement.TryGetProperty("fixedlocation", out var fixedfooterElement))
                    {
                        foreach (var _fixedfooter in fixedfooterElement.EnumerateArray())
                        {
                            var _sectionfooter = new SectionFooter
                            {
                                x = int.Parse(_fixedfooter.GetProperty("footerx").ToString()),
                                y = int.Parse(_fixedfooter.GetProperty("footery").ToString()),
                                width = int.Parse(_fixedfooter.GetProperty("footerwidth").ToString()),
                                height = int.Parse(_fixedfooter.GetProperty("footerheight").ToString())
                            };

                            var pageNumbers = _fixedfooter.TryGetProperty("location", out var locProp) &&
                                                            !string.IsNullOrWhiteSpace(locProp.GetString())
                                                            ? locProp.GetString().ToLowerInvariant().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                            : new string[0];

                            foreach (var page in pageNumbers)
                            {
                                if (!ModelSettings.clsSectionFooter.fixedLocationFooter.ContainsKey(page))
                                {
                                    ModelSettings.clsSectionFooter.fixedLocationFooter.Add(page, _sectionfooter);
                                }
                            }
                        }
                    }

                }

            }
            if (root.TryGetProperty("byLineOverrides", out var byLineOverridesElement))
            {
                foreach (var section in byLineOverridesElement.EnumerateObject())
                {
                    Dictionary<string, int> priorities = new Dictionary<string, int>();
                    foreach (var priority in section.Value.EnumerateArray())
                    {
                        foreach (var property in priority.EnumerateObject())
                        {
                            priorities.Add(property.Name, property.Value.GetInt32());
                        }
                    }
                    ModelSettings.byLineOverride.Add(section.Name.ToLower(), priorities);
                }
            }
            //UM: editorails ads
            if (root.TryGetProperty("fillerSetting", out var fillerSettingElement))
            {
                JsonElement isEnabled;
                JsonElement allowPlacingFillerAboveTheAd;
                JsonElement minimumSpaceInAdAndFiller;

                if (fillerSettingElement.TryGetProperty("allowFillerPlacement", out isEnabled))
                {

                    ModelSettings.bPlacingFillerAllowed = bool.Parse(fillerSettingElement.GetProperty("allowFillerPlacement").ToString());
                }
                if (ModelSettings.bPlacingFillerAllowed && fillerSettingElement.TryGetProperty("allowPlacingFillerAboveTheAd", out allowPlacingFillerAboveTheAd))
                {
                    ModelSettings.bAllowPlacingFillerAboveTheAd = bool.Parse(fillerSettingElement.GetProperty("allowPlacingFillerAboveTheAd").ToString());
                }
                if (ModelSettings.bPlacingFillerAllowed && fillerSettingElement.TryGetProperty("minimumSpaceBetweenAdAndFiller", out minimumSpaceInAdAndFiller))
                {
                    ModelSettings.minimumSpaceInAdAndFiller = int.Parse(fillerSettingElement.GetProperty("minimumSpaceBetweenAdAndFiller").ToString());
                }
                if (ModelSettings.bPlacingFillerAllowed && fillerSettingElement.TryGetProperty("fillerAlignment", out var fillerAlignmentElement))
                {
                    foreach (JsonProperty property in fillerAlignmentElement.EnumerateObject())
                    {
                        if (property.Name != "top" && property.Name != "bottom")
                        {
                            Log.Information("{name} alignment is not valid for filler ads! only top and bottom alignment are supported! applying bottom alignment as default", property.Name);
                        }
                    }

                    if (fillerAlignmentElement.TryGetProperty("top", out var topElement))
                    {
                        List<string> topList = new List<string>();
                        foreach (var item in topElement.EnumerateArray())
                        {
                            topList.Add(item.GetString());
                        }
                        ModelSettings.fillerAlignment.Add("top", topList);
                    }

                    if (fillerAlignmentElement.TryGetProperty("bottom", out var bottomElement))
                    {
                        List<string> bottomList = new List<string>();
                        foreach (var item in bottomElement.EnumerateArray())
                        {
                            bottomList.Add(item.GetString());
                        }
                        ModelSettings.fillerAlignment.Add("bottom", bottomList);
                    }
                }
            }
            //Umesh : FLOW-291
            if (root.TryGetProperty("SectionReMap", out var sectionMappingElement))
            {
                foreach (JsonElement ruleElement in sectionMappingElement.EnumerateArray())
                {
                    string key = ruleElement.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
                    string value = ruleElement.GetProperty("value").GetString();
                    string pattern = ruleElement.TryGetProperty("pattern", out var patternElement) ? patternElement.GetString() : null;
                    SectionMapping obj = new SectionMapping(key, value, pattern);
                    if (ruleElement.TryGetProperty("exclude", out var excludeElement) && excludeElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement excludeItem in excludeElement.EnumerateArray())
                        {
                            obj.exclude.Add(excludeItem.GetString());
                        }
                    }
                    ModelSettings.sectionMappingList.Add(obj);
                }
            }


            if (pageElement.TryGetProperty("extralineaboveHeadline", out var extralineaboveheadlineElement))
            {
                ModelSettings.extralineaboveHeadline = int.Parse(extralineaboveheadlineElement.ToString());
            }

            if (root.TryGetProperty("optimization", out var optimizationElement))
            {
                ModelSettings.optimizeArticlePermutations = bool.Parse(optimizationElement.GetProperty("enabled").ToString());
                ModelSettings.minarticleForOptimization = int.Parse(optimizationElement.GetProperty("minarticles").ToString());

            }

            if (ModelSettings.bTextWrapEnabled)
            {
                if (root.TryGetProperty("textwrapArticles", out var textwrapArticlesElement))
                {
                    ModelSettings.textwrapSettings = textwrapArticlesElement.Deserialize<ModelSettings.TextWrapArticles>();
                }
            }

            if (pageElement.TryGetProperty("quarterpageAdEnabled", out var quarterpageAdEnabledElement))
            {
                ModelSettings.quarterpageAdEnabled = bool.Parse(quarterpageAdEnabledElement.ToString());
            }
            if (pageElement.TryGetProperty("enableLargeHeadlinesForLowPriority", out var enableLargeHeadlinesForLowPriorityElement))
            {
                ModelSettings.enableLargeHeadlinesForLowPriority = bool.Parse(enableLargeHeadlinesForLowPriorityElement.ToString());
            }

            if (ModelSettings.hasdoubletruck == 1)
            {
                ModelSettings.clsDoubleTruck.mainarticleminsize = int.Parse(truckElement.GetProperty("mainarticleminsize").ToString());
                ModelSettings.clsDoubleTruck.mainarticlemaxsize = int.Parse(truckElement.GetProperty("mainarticlemaxsize").ToString());
                ModelSettings.clsDoubleTruck.minimages = int.Parse(truckElement.GetProperty("minimages").ToString());
                ModelSettings.clsDoubleTruck.maximages = int.Parse(truckElement.GetProperty("maximages").ToString());
                ModelSettings.clsDoubleTruck.mainmincolumns = int.Parse(truckElement.GetProperty("mainimageminsize").ToString());
                ModelSettings.clsDoubleTruck.mainmaxcolumns = int.Parse(truckElement.GetProperty("mainimagemaxsize").ToString());
                ModelSettings.clsDoubleTruck.submincolumns = int.Parse(truckElement.GetProperty("subimageminsize").ToString());
                ModelSettings.clsDoubleTruck.submaxcolumns = int.Parse(truckElement.GetProperty("subimagemaxsize").ToString());
                ModelSettings.clsDoubleTruck.minfactsAvailable = int.Parse(truckElement.GetProperty("minfacts").ToString());

                string[] _sfctvals = truckElement.GetProperty("availablefactsizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int[] _ifctvals = new int[_sfctvals.Length];
                for (int _i = 0; _i < _sfctvals.Length; _i++)
                {
                    _ifctvals[_i] = int.Parse(_sfctvals[_i].Trim());
                }
                HashSet<int> _facthashset = _ifctvals.ToHashSet().OrderBy(x => x).ToHashSet();
                ModelSettings.clsDoubleTruck.factsizes = _facthashset;

                JsonElement headlineElement, layoutelement;
                if (truckElement.TryGetProperty("availableheadlinesizes", out headlineElement))
                {
                    string[] _sheadlinevals = headlineElement.ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    int[] _iheadlinevals = new int[_sheadlinevals.Length];
                    for (int _i = 0; _i < _iheadlinevals.Length; _i++)
                    {
                        _iheadlinevals[_i] = int.Parse(_sheadlinevals[_i].Trim());
                    }
                    HashSet<int> _headlinehashset = _iheadlinevals.ToHashSet().OrderBy(x => x).ToHashSet();
                    ModelSettings.clsDoubleTruck.headlinesizes = _headlinehashset;
                }

                if (truckElement.TryGetProperty("layout", out layoutelement))
                {
                    ModelSettings.clsDoubleTruck.layouttype = layoutelement.ToString();
                }

                ModelSettings.clsDoubleTruck.maxcitationsize = int.Parse(truckElement.GetProperty("maxcitationsize").ToString());
                ModelSettings.clsDoubleTruck.croppercentage = double.Parse(truckElement.GetProperty("cropPercentage").ToString());
                ModelSettings.clsDoubleTruck.maincroppercentage = double.Parse(truckElement.GetProperty("maincropPercentage").ToString());

                JsonElement relatedArticleElement;
                if (truckElement.TryGetProperty("relatedarticles", out relatedArticleElement))
                {
                    ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles = int.Parse(relatedArticleElement.GetProperty("maxarticles").ToString());
                    string[] _scanvassizes = relatedArticleElement.GetProperty("availablecanvasizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    int[] _icanvassizes = new int[_scanvassizes.Length];
                    for (int _i = 0; _i < _icanvassizes.Length; _i++)
                    {
                        _icanvassizes[_i] = int.Parse(_scanvassizes[_i].Trim());
                    }

                    HashSet<int> _canvassizes = _icanvassizes.ToHashSet();
                    ModelSettings.clsDoubleTruck.clsRelatedArticles.availablecanvasizes = _canvassizes;

                    if (relatedArticleElement.TryGetProperty("excludemultipleOneSize", out var excludemultipleOneSizeElement))
                        ModelSettings.clsDoubleTruck.clsRelatedArticles.excludemultipleOneSize = bool.Parse(excludemultipleOneSizeElement.ToString());
                    else
                        ModelSettings.clsDoubleTruck.clsRelatedArticles.excludemultipleOneSize = false;
                    //string[] _sexcludearticlesizes = relatedArticleElement.GetProperty("excludearticleSizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    //int[] _iexcludearticlesizes = new int[_sexcludearticlesizes.Length];
                    //for (int _i = 0; _i < _iexcludearticlesizes.Length; _i++)
                    //{
                    //    _iexcludearticlesizes[_i] = int.Parse(_sexcludearticlesizes[_i].Trim());
                    //}
                    //ModelSettings.clsDoubleTruck.clsRelatedArticles.exludearticleSizes = _iexcludearticlesizes.ToHashSet();

                }

                JsonElement headlinetypeElement;
                if (truckElement.TryGetProperty("headlinetype", out headlinetypeElement))
                {
                    string[] headlinetypestring = headlinetypeElement.ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    ModelSettings.clsDoubleTruck.headlinetypes = headlinetypestring.ToHashSet();
                }
                ModelSettings.clsDoubleTruck.enableimagesorting = bool.Parse(truckElement.GetProperty("enableimagesorting").ToString());

                JsonElement maxheadlinesizeElement = truckElement.GetProperty("maxheadlinesize");
                foreach (var _maxhlsizes in maxheadlinesizeElement.EnumerateArray())
                {
                    int _hlcols = int.Parse(_maxhlsizes.GetProperty("columns").ToString());
                    int _hltypo = int.Parse(_maxhlsizes.GetProperty("typelines").ToString());
                    ModelSettings.clsDoubleTruck.maxheadlinesize.Add(_hlcols, _hltypo);

                }

                if (truckElement.TryGetProperty("mainImageCaptionOnLeftPage", out var mainImageCaptionOnLeftPageElement))
                {
                    ModelSettings.clsDoubleTruck.mainImageCaptionOnLeftPage = bool.Parse(mainImageCaptionOnLeftPageElement.ToString());
                }

            }
            foreach (var article in articlesElement.EnumerateArray())
            {
                string _priority = article.GetProperty("priority").ToString();
                int _ipriority = 1;
                if (_priority == "A")
                    _ipriority = 5;
                if (_priority == "B")
                    _ipriority = 4;
                if (_priority == "C")
                    _ipriority = 3;
                if (_priority == "D")
                    _ipriority = 2;
                if (_priority == "E")
                    _ipriority = 1;
                if (_priority == "X")
                    _ipriority = 0;

                BoxSettings _settings = new BoxSettings();
                if (_ipriority == 5)
                {
                    if (article.TryGetProperty("excludeColumnForNonAdPages", out var excludeColumns))
                    {
                        string[] excludearrvals = excludeColumns.ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        int[] _iexcludevals = new int[excludearrvals.Length];
                        for (int _i = 0; _i < excludearrvals.Length; _i++)
                        {
                            _iexcludevals[_i] = int.Parse(excludearrvals[_i].Trim());
                        }
                        HashSet<int> _excludehashset = _iexcludevals.ToHashSet().OrderBy(x => x).ToHashSet();
                        _settings.excludeColumnForNonAdPages = _excludehashset;
                    }
                }
                //_settings.minarticlecolumns = int.Parse(article.GetProperty("mincolumns").ToString());
                //_settings.maxarticlecolumns = int.Parse(article.GetProperty("maxcolumns").ToString());
                string[] arrvals = article.GetProperty("availableColumnSizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int[] _ivals = new int[arrvals.Length];
                for (int _i = 0; _i < arrvals.Length; _i++)
                {
                    _ivals[_i] = int.Parse(arrvals[_i].Trim());
                }
                HashSet<int> _hashset = _ivals.ToHashSet().OrderBy(x => x).ToHashSet();
                _settings.articleLengths = _hashset;

                _settings.minfactsAvailable = int.Parse(article.GetProperty("minfacts").ToString());

                JsonElement imageElement = article.GetProperty("images");
                string[] _sfctvals = article.GetProperty("availablefactsizes").ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int[] _ifctvals = new int[_sfctvals.Length];
                for (int _i = 0; _i < _sfctvals.Length; _i++)
                {
                    _ifctvals[_i] = int.Parse(_sfctvals[_i].Trim());
                }
                HashSet<int> _facthashset = _ifctvals.ToHashSet().OrderBy(x => x).ToHashSet();
                _settings.factsizes = _facthashset;

                if (article.TryGetProperty("headlinetype", out var headlinetypeelement))
                {
                    _settings.headlinetype = headlinetypeelement.ToString();
                }

                _settings.minimages = int.Parse(imageElement.GetProperty("minimages").ToString());
                _settings.maximages = int.Parse(imageElement.GetProperty("maximages").ToString());
                _settings.mainmincolumns = int.Parse(imageElement.GetProperty("mainmincolumns").ToString());
                _settings.mainmaxcolumns = int.Parse(imageElement.GetProperty("mainmaxcolumns").ToString());
                _settings.submincolumns = int.Parse(imageElement.GetProperty("submincolumns").ToString());
                _settings.submaxcolumns = int.Parse(imageElement.GetProperty("submaxcolumns").ToString());
                //_settings.maxfactsize = int.Parse(imageElement.GetProperty("maxfactsize").ToString());
                _settings.maxcitationsize = int.Parse(imageElement.GetProperty("maxcitationsize").ToString());


                ModelSettings.boxsettings.Add(_ipriority, _settings);

                JsonElement _sectionElement;
                bool _sectionexists = article.TryGetProperty("sections", out _sectionElement);

                if (_sectionexists)
                {
                    Hashtable ht = new Hashtable();
                    foreach (var _element in _sectionElement.EnumerateArray())
                    {
                        string _section = _element.GetProperty("section").ToString();
                        string _invalidcolumns = _element.GetProperty("invalidcolumns").ToString();
                        ht.Add(_section, _invalidcolumns);

                    }
                    ModelSettings.invalidColSettings.Add(_ipriority, ht);
                }
            }

            if (root.TryGetProperty("articletypes", out var articletypeelement))
            {
                foreach (var typelement in articletypeelement.EnumerateArray())
                {
                    string _articletype = typelement.GetProperty("articletype").ToString();

                    if (_articletype.Equals("briefs", StringComparison.OrdinalIgnoreCase))
                    {
                        if (typelement.TryGetProperty("overset", out var oversetElement))
                        {
                            ModelSettings.briefSettings.overset = bool.TryParse(oversetElement.ToString(), out bool overset) ? overset : false;
                        }
                        if (typelement.TryGetProperty("width", out var widthElement))
                        {
                            ModelSettings.briefSettings.width = int.TryParse(widthElement.ToString(), out int width) ? width : 1;
                        }
                        continue;
                    }

                    if (_articletype.Equals("letters", StringComparison.OrdinalIgnoreCase))
                    {
                        if (typelement.TryGetProperty("imageAlignment", out var imageAlignment))
                        {
                            ModelSettings.letterSettings.imageAlignment = imageAlignment.GetString().ToLower();
                            if (!new[] { "center", "right" }.Contains(ModelSettings.letterSettings.imageAlignment))
                            {
                                ModelSettings.letterSettings.imageAlignment = "center";
                            }

                            if (typelement.TryGetProperty("imageSize", out var imageSize))
                            {
                                ModelSettings.letterSettings.imageSize = imageSize.GetInt32();
                                if (ModelSettings.letterSettings.imageSize > ModelSettings.canvaswidth)
                                {
                                    ModelSettings.letterSettings.imageSize = ModelSettings.canvaswidth;
                                }

                                if (ModelSettings.letterSettings.imageSize < 0)
                                {
                                    ModelSettings.letterSettings.imageSize = 2;
                                }
                            }

                            if (typelement.TryGetProperty("headlineSizes", out var headlineSizes))
                            {
                                ModelSettings.letterSettings.headlineSizes = headlineSizes.Deserialize<List<string>>();
                            }
                        }
                        continue;
                    }

                    if (_articletype.Equals("picturelead", StringComparison.OrdinalIgnoreCase))
                    {
                        if (typelement.TryGetProperty("layout", out var layoutElement))
                            ModelSettings.pictureLeadArticleTypes = layoutElement.Deserialize<List<ModelSettings.PictureLeadArticleType>>();
                        continue;
                    }
                    ArticleTypeSettings _articlesettings = new ArticleTypeSettings();

                    JsonElement sizeelement = typelement.GetProperty("size");
                    _articlesettings.articlesize.x = int.Parse(sizeelement.GetProperty("x").ToString());
                    _articlesettings.articlesize.y = int.Parse(sizeelement.GetProperty("y").ToString());
                    _articlesettings.articlesize.width = int.Parse(sizeelement.GetProperty("width").ToString());
                    _articlesettings.articlesize.height = int.Parse(sizeelement.GetProperty("height").ToString());

                    if (typelement.TryGetProperty("image", out var imageelement))
                    {
                        JsonElement imagesizeselement = imageelement.GetProperty("sizes");

                        foreach (var imagesizeelement in imagesizeselement.EnumerateArray())
                        {
                            itemsize _size = new itemsize();
                            _size.x = int.Parse(imagesizeelement.GetProperty("x").ToString());
                            _size.y = int.Parse(imagesizeelement.GetProperty("y").ToString());
                            _size.width = int.Parse(imagesizeelement.GetProperty("width").ToString());
                            _size.height = int.Parse(imagesizeelement.GetProperty("height").ToString());
                            _articlesettings.imagesizes.Add(_size);
                        }
                    }

                    ModelSettings.articletypesettings.Add(_articletype, _articlesettings);
                }
            }

            if (ModelSettings.hasmultispread == 1)
                ModelSettings.multiSpreadSettings = root.TryGetProperty("multispread", out var magazineElement) ? magazineElement.Deserialize<MultiSpreadSettings>() : new MultiSpreadSettings();

            if (ModelSettings.picturestoriesenabled)
            {
                if (root.TryGetProperty("picturestories", out var picturestorysettingsElement))
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    ModelSettings.clsPictureArticleSettings = picturestorysettingsElement.Deserialize<PictureArticleSettings>(options);
                }
            }

            if (ModelSettings.picturestoriesdtenabled)
            {
                if (root.TryGetProperty("picturestoriesdt", out var picturestoryDTsettingsElement))
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    ModelSettings.clsPictureArticleDTSettings = picturestoryDTsettingsElement.Deserialize<PictureArticleDTSettings>(options);
                }
            }

            if (root.TryGetProperty("Mugshot", out var mshot))
            {
                ModelSettings.mugshotSetting = mshot.Deserialize<Mugshot>();
            }

            if (root.TryGetProperty("penalties", out var penaltiesElement))
            {
                foreach (var penaltyElement in penaltiesElement.EnumerateArray())
                {
                    int _pr = Helper.GetNumericalPriority(penaltyElement.GetProperty("priority").ToString());
                    int _si = penaltyElement.GetProperty("size").GetInt32();
                    int _pe = penaltyElement.GetProperty("penaltyperc").GetInt32();
                    ModelSettings.lstPenalties.Add(new Penalties() { penaltyperc = _pe, priority = _pr, size = _si });
                }

            }

            LoadRewardsFromModeltuning(root);


            if (ModelSettings.bCustomPlacementEnabled && root.TryGetProperty("placementrules", out var placementrulesElement))
            {
                if (placementrulesElement.TryGetProperty("defaultoddpage", out var defaultoddruleElement))
                    ModelSettings.placementRules.defaultoddpage = defaultoddruleElement.GetString().ToLower();

                if (placementrulesElement.TryGetProperty("defaultevenpage", out var defaultevenruleElement))
                    ModelSettings.placementRules.defaultevenpage = defaultevenruleElement.GetString().ToLower();

                if (placementrulesElement.TryGetProperty("oddpage", out var oddpageElement))
                {
                    string customrule = oddpageElement.GetProperty("rule").GetString();
                    foreach (var _section in oddpageElement.GetProperty("sections").EnumerateArray())
                    {
                        ModelSettings.placementRules.oddpagerule.Add(new SectionPlacemntRules() { rule = customrule, section = _section.ToString() });
                    }
                }

                if (placementrulesElement.TryGetProperty("evenpage", out var evenpageElement))
                {
                    string customrule = evenpageElement.GetProperty("rule").GetString();
                    foreach (var _section in evenpageElement.GetProperty("sections").EnumerateArray())
                    {
                        ModelSettings.placementRules.evenpagerule.Add(new SectionPlacemntRules() { rule = customrule, section = _section.ToString() });
                    }
                }

            }

        }
    }

    private string TryLoadModelTuningContent(string? modelSettingCLI, AppSettings? settings, string? pubName)
    {
        string modelstring = "";
        if (string.IsNullOrEmpty(modelSettingCLI) && settings != null && !string.IsNullOrEmpty(pubName))
        {
            if (settings?.ReadMTFromS3 == true)
            {
                modelstring = LoadModelSettingsFromS3(pubName, settings).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(modelstring))
                {
                    throw new Exception("Couldn't find the modelsetting file from S3");
                }
            }
            else if (settings?.ReadMTFromS3 == false)
            {
                string _modelsettingfile = settings.ModelTuningLocations[sPubName];

                Log.Information("ModelSettingFile: {fileName}", _modelsettingfile);
                if (_modelsettingfile == null || _modelsettingfile.Length == 0 || !System.IO.File.Exists(_modelsettingfile))
                {
                    throw new Exception("Couldn't find the modelsetting file from Local system");
                }
                modelstring = System.IO.File.ReadAllText(_modelsettingfile);
            }
        }
        else
        {
            modelstring = System.IO.File.ReadAllText(modelSettingCLI);
        }
        return modelstring;
    }

    private void GetArticleCombinationforOneImage(Box _box, List<Box> _boxlist, List<Image> _tempimagelist, List<Image> _tempfctList, List<Image> _tempcitationList, int _minfctused)
    {
        int _fctcount = 0;
        if (_tempfctList != null && _tempfctList.Count > 0)
            _fctcount = 1;

        if (_fctcount >= 1)
        {
            if (_tempcitationList != null)
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                    {
                        foreach (Image _citimage in _tempcitationList)
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _allfacts, _citimage);
                            _boxlist.AddRange(_tempboxlist);
                        }
                    }
                }
            }
            else
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _allfacts, null);
                        _boxlist.AddRange(_tempboxlist);
                    }

                }
            }
            if (_minfctused == 0) //factboxes are optional
            {
                List<Image> _tmpfctList = new List<Image>();
                if (_tempcitationList != null)
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        foreach (Image _citimage in _tempcitationList)
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _tmpfctList, _citimage);
                            _boxlist.AddRange(_tempboxlist);
                        }
                    }
                }
                else
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _tmpfctList, null);
                        _boxlist.AddRange(_tempboxlist);
                    }

                }
            }
        }
        else
        {
            List<Image> _tmpfctList = new List<Image>();
            if (_tempcitationList != null)
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    foreach (Image _citimage in _tempcitationList)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _tmpfctList, _citimage);
                        _boxlist.AddRange(_tempboxlist);
                    }
                }
            }
            else
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _tmpfctList, null);
                    _boxlist.AddRange(_tempboxlist);
                }
            }
        }
    }

    private void GetArticleCombinationforNoImage(Box _box, List<Box> _boxlist, List<Image> _tempfctList, List<Image> _tempcitationList, int _minfctused)
    {
        int _fctcount = 0;
        if (_tempfctList != null && _tempfctList.Count > 0)
            _fctcount = 1;

        if (_fctcount >= 1)
        {
            if (_tempcitationList != null)
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _citimage in _tempcitationList)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizesNoMain(_box, _allfacts, _citimage, mugshots: null);
                        _boxlist.AddRange(_tempboxlist);
                    }

                }
            }
            else
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    {
                        List<Box> _tempboxlist = FindAllArticleSizesNoMain(_box, _allfacts, null, mugshots: null);
                        _boxlist.AddRange(_tempboxlist);
                    }

                }
            }
            if (_minfctused == 0) //factboxes are optional
            {
                List<Image> _tmpfctList = new List<Image>();
                if (_tempcitationList != null)
                {
                    foreach (Image _citimage in _tempcitationList)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizesNoMain(_box, _tmpfctList, _citimage, mugshots: null);
                        _boxlist.AddRange(_tempboxlist);
                    }
                }
            }
        }
        else
        {
            List<Image> _tmpfctList = new List<Image>();
            if (_tempcitationList != null)
            {
                foreach (Image _citimage in _tempcitationList)
                {
                    List<Box> _tempboxlist = FindAllArticleSizesNoMain(_box, _tmpfctList, _citimage, mugshots: null);
                    _boxlist.AddRange(_tempboxlist);
                }
            }
            else
            {
                List<Box> _tempboxlist = FindAllArticleSizesNoMain(_box, _tmpfctList, null, mugshots: null);
                _boxlist.AddRange(_tempboxlist);
            }
        }
    }
    private void GetArticleCombinationforNImages(Box _box, List<Box> _boxlist, List<Image> _tempimagelist, List<List<Image>> _lstlstImages, List<Image> _tempfctList, List<Image> _tempcitationList, int _minfctused)
    {
        List<Image> _lst1 = _lstlstImages[0];
        List<Image> _lst2 = _lstlstImages[1];
        List<Image> _lst3 = _lstlstImages[2];


        int _fctcount = 0;
        if (_tempfctList != null && _tempfctList.Count > 0)
            _fctcount = 1;

        if (_fctcount >= 1)
        {
            if (_minfctused == 1)
            {
                //_tempcitationList = null;
                //POK: For 3 images either of fact or citation will be considered

                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                        foreach (Image _nimg1 in _lst1)
                            foreach (Image _nimg2 in _lst2)
                                foreach (Image _nimg3 in _lst3)
                                {
                                    if ((_nimg1.length == _nimg2.length && _nimg1.length == _nimg3.length) ||
                                        (_nimg1.width == _nimg2.width))
                                    {
                                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _nimg3, _allfacts, null);
                                        if (_tempboxlist.Count > 0)
                                            _boxlist.AddRange(_tempboxlist);
                                    }
                                }
                }
            }

            if (_minfctused == 0) //factboxes are optional
            {
                List<Image> _tmpfctList = null;
                if (_tempcitationList != null)
                {
                    foreach (Image _nimg in _tempimagelist)
                        foreach (Image _nimg1 in _lst1)
                            foreach (Image _nimg2 in _lst2)
                                foreach (Image _nimg3 in _lst3)
                                {
                                    foreach (Image _citimage in _tempcitationList)
                                    {
                                        if ((_nimg1.length == _nimg2.length && _nimg1.length == _nimg3.length) ||
                                            (_nimg1.width == _nimg2.width))
                                        {
                                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _nimg3, _tmpfctList, _citimage);
                                            _boxlist.AddRange(_tempboxlist);
                                        }
                                    }
                                }
                }
                else
                {
                    foreach (Image _nimg in _tempimagelist)
                        foreach (Image _nimg1 in _lst1)
                            foreach (Image _nimg2 in _lst2)
                                foreach (Image _nimg3 in _lst3)
                                {
                                    if ((_nimg1.length == _nimg2.length && _nimg1.length == _nimg3.length) ||
                                        (_nimg1.width == _nimg2.width))
                                    {
                                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _nimg3, null, null);
                                        if (_tempboxlist.Count > 0)
                                            _boxlist.AddRange(_tempboxlist);
                                    }
                                }

                }
            }
        }
        else
        {
            List<Image> _tmpfctList = null;
            if (_tempcitationList != null)
            {
                foreach (Image _nimg in _tempimagelist)
                    foreach (Image _nimg1 in _lst1)
                        foreach (Image _nimg2 in _lst2)
                            foreach (Image _nimg3 in _lst3)
                            {
                                foreach (Image _citimage in _tempcitationList)
                                {
                                    if ((_nimg1.length == _nimg2.length && _nimg1.length == _nimg3.length) ||
                                        (_nimg1.width == _nimg2.width))
                                    {
                                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _nimg3, _tmpfctList, _citimage);
                                        _boxlist.AddRange(_tempboxlist);
                                    }
                                }
                            }
            }
            else
            {
                foreach (Image _nimg in _tempimagelist)
                    foreach (Image _nimg1 in _lst1)
                        foreach (Image _nimg2 in _lst2)
                            foreach (Image _nimg3 in _lst3)
                            {
                                if ((_nimg1.length == _nimg2.length && _nimg1.length == _nimg3.length) ||
                                    (_nimg1.width == _nimg2.width))
                                {
                                    List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _nimg3, null, null);
                                    if (_tempboxlist.Count > 0)
                                        _boxlist.AddRange(_tempboxlist);
                                }
                            }

            }
        }
    }
    private void GetArticleCombinationforThreeImages(Box _box, List<Box> _boxlist, List<Image> _tempimagelist, List<Image> _tempsubimagelist, List<Image> _tempsubimagelist2, List<Image> _tempfctList, List<Image> _tempcitationList, int _minfctused)
    {
        int _fctcount = 0;
        if (_tempfctList != null && _tempfctList.Count > 0)
            _fctcount = 1;

        if (_fctcount >= 1)
        {
            if (_minfctused == 1)
            {
                //_tempcitationList = null;
                //POK: For 3 images either of fact or citation will be considered

                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                    {
                        //choosing the relational operator based on whether same size subimages are allowed or not
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                            foreach (Image _nimg2 in _tempsubimagelist2.Where(subImageFilter))
                            {
                                List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _allfacts, null);
                                if (_tempboxlist.Count > 0)
                                    _boxlist.AddRange(_tempboxlist);
                            }
                    }
                }
            }

            if (_minfctused == 0) //factboxes are optional
            {
                List<Image> _tmpfctList = null;
                if (_tempcitationList != null)
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                            foreach (Image _nimg2 in _tempsubimagelist2.Where(subImageFilter))
                            {
                                foreach (Image _citimage in _tempcitationList)
                                {
                                    List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _tmpfctList, _citimage);
                                    _boxlist.AddRange(_tempboxlist);
                                }
                            }
                    }
                }
                else
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                            foreach (Image _nimg2 in _tempsubimagelist2.Where(subImageFilter))
                            {
                                List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _tmpfctList, null);
                                _boxlist.AddRange(_tempboxlist);
                            }
                    }
                }
            }
        }
        else
        {
            List<Image> _tmpfctList = null;
            if (_tempcitationList != null)
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);
                    foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        foreach (Image _nimg2 in _tempsubimagelist2.Where(subImageFilter))
                        {
                            foreach (Image _citimage in _tempcitationList)
                            {
                                List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _tmpfctList, _citimage);
                                _boxlist.AddRange(_tempboxlist);
                            }
                        }
                }
            }
            else
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                    foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        foreach (Image _nimg2 in _tempsubimagelist2.Where(subImageFilter))
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _nimg2, _tmpfctList, null);
                            _boxlist.AddRange(_tempboxlist);
                        }
                }
            }
        }
    }

    private void GetArticleCombinationforTwoImages(Box _box, List<Box> _boxlist, List<Image> _tempimagelist, List<Image> _tempsubimagelist, List<Image> _tempfctList, List<Image> _tempcitationList, int _minfctused)
    {

        int _fctcount = 0;
        if (_box.Id == "6c9509cb-9559-415d-a7d8-d54fbbfe103c")
            _fctcount = 0;
        if (_tempfctList != null && _tempfctList.Count > 0)
            _fctcount = 1;

        if (_fctcount >= 1)
        {
            if (_tempcitationList != null)
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);// ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        {
                            foreach (Image _citimage in _tempcitationList)
                            {
                                List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _allfacts, _citimage);
                                _boxlist.AddRange(_tempboxlist);
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (Image _fct in _tempfctList)
                {
                    List<Image> _allfacts = new List<Image>();
                    _allfacts.Add(_fct);
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _allfacts, null);
                            _boxlist.AddRange(_tempboxlist);
                        }
                    }
                }
            }
            if (_minfctused == 0) //factboxes are optional
            {
                List<Image> _tmpfctList = null;
                if (_tempcitationList != null)
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        {
                            foreach (Image _citimage in _tempcitationList)
                            {
                                List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _tmpfctList, _citimage);
                                _boxlist.AddRange(_tempboxlist);
                            }
                        }
                    }
                }
                else
                {
                    foreach (Image _nimg in _tempimagelist)
                    {
                        var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                        foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _tmpfctList, null);
                            _boxlist.AddRange(_tempboxlist);
                        }

                    }
                }
            }
        }
        else
        {
            List<Image> _tmpfctList = null;
            if (_tempcitationList != null)
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                    foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                    {
                        foreach (Image _citimage in _tempcitationList)
                        {
                            List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _tmpfctList, _citimage);
                            _boxlist.AddRange(_tempboxlist);
                        }
                    }
                }
            }
            else
            {
                foreach (Image _nimg in _tempimagelist)
                {
                    var subImageFilter = Helper.GetSubImageFilter(_nimg.imagetype == "mugshot", _nimg);//ModelSettings.samesizesubimageallowed ? (Func<Image, bool>)(x => x.width <= _nimg.width) : (x => x.width < _nimg.width);
                    foreach (Image _nimg1 in _tempsubimagelist.Where(subImageFilter))
                    {
                        List<Box> _tempboxlist = FindAllArticleSizes(_box, _nimg, _nimg1, _tmpfctList, null);
                        _boxlist.AddRange(_tempboxlist);
                    }
                }
            }
        }
    }

    private List<Box> FindArticlePermutationsForDoubleTruck(Box _box, PageInfo _pageInfo)
    {
        List<Box> _boxlist = new List<Box>();

        int _minfctused = ModelSettings.clsDoubleTruck.minfactsAvailable;

        int _fctcount = _box.factList == null ? 0 : _box.factList.Count;
        int _citationcount = _box.citationList == null ? 0 : _box.citationList.Count;

        if (_minfctused == 0 || _fctcount == 0)
            _boxlist = FindSizesForDoubleTruck(_box, null);

        //List<Image> _tempcitationList = null;
        //List<Image> _tempfctList = null;
        //if (_citationcount > 0)
        //    _tempcitationList = Image.GetAllPossibleImageSizesDT(_box.citationList[0], _box, 0);
        //if (_fctcount > 0)
        //    _tempfctList = Image.GetAllPossibleImageSizesDT(_box.factList[0], _box, 0);

        if (_fctcount > 0)
            _boxlist.AddRange(FindSizesForDoubleTruck(_box, _box.factList));

        //RP: FLOW-278: Kickers are already added to the start location and hence shouldn't be added here
        //if (ModelSettings.haskicker == 1)
        //{
        //    foreach (var _tempbox in _boxlist)
        //    {
        //        if (kickersmap.Count > 0 && kickersmap[_tempbox.Id] != null)
        //        {
        //            int kickerlength = (int)((Kicker)kickersmap[_tempbox.Id]).collinemap[(int)_tempbox.width];
        //            _tempbox.kickerlength = kickerlength;
        //            _tempbox.length = _tempbox.length + kickerlength;
        //        }
        //        _i++;
        //    }
        //}

        List<Box> newBoxList = new List<Box>();
        Headline _headline = (Headline)headlines[_box.Id];
        //Add small headline
        foreach (Box _tbox in _boxlist)
        {
            foreach (int _hlwidth in ModelSettings.clsDoubleTruck.headlinesizes)
            {
                int _hsmall = _headline.GetHeadlineHeight("small", _hlwidth);
                if (_hsmall <= 0)
                    continue;
                //int _hmedium = _headline.GetHeadlineHeight("medium", (int)_tbox.width);
                //int _hlarge = _headline.GetHeadlineHeight("large", 2*canvasx);

                Box _tempbox = Helper.DeepCloneBox(_tbox);
                double _newarea = _tempbox.width * _tempbox.length + _hsmall * _hlwidth;
                int _newheight = (int)Math.Ceiling(_newarea / _tempbox.width);
                _tempbox.headlinecaption = "small";
                _tempbox.headlinelength = _hsmall;
                _tempbox.length = _newheight;

                if (_tempbox.length - _tempbox.headlinelength < _tempbox.preamble + _tempbox.byline)
                {
                    _tempbox.length = _tempbox.headlinelength + _tempbox.preamble + _tempbox.byline;
                }

                if (_tempbox.length <= canvasz - _pageInfo.sectionheaderheight)
                    newBoxList.Add(_tempbox);
            }

        }


        int _i = 1;
        foreach (var _tempbox in newBoxList)
        {
            _tempbox.volume = _tempbox.length * _tempbox.width;
            _tempbox.usedimagecount = _tempbox.usedImageList == null ? 0 : _tempbox.usedImageList.Count();

            _tempbox.runid = _i;
            _i++;
        }

        return newBoxList;
    }

    private List<Box> FindArticleSizeForArticleTypes(Box _box, String _articletype)
    {
        List<Box> _boxlist = new List<Box>();

        if (_articletype == "loft")
        {
            ArticleTypeSettings _settings = ModelSettings.articletypesettings["loft"];
            int _x = _settings.articlesize.x;
            int _y = _settings.articlesize.y;
            int _width = _settings.articlesize.width;
            int _height = _settings.articlesize.height;

            Box _b = Helper.CustomCloneBox(_box);
            _b.usedImageList = new List<Image>();
            _b.volume = _width * _height;
            _b.position = new Node() { pos_x = _x, pos_z = _y };
            _b.width = _width;
            _b.length = _height;
            Headline _headline = (Headline)headlines[_box.Id];

            int _totarea2colimage, _totarea1colimage;
            bool _articlegenerated = false;
            if (_box.imageList.Count > 0 && _settings.imagesizes.Count > 0)
            {
                foreach (var _imgsetting in _settings.imagesizes.OrderByDescending(x => x.width))
                {
                    Image _img = Helper.CustomCloneImage(_box.imageList[0]);
                    int _totalarea = (int)(_box.origArea);
                    int _hllength = _headline.GetHeadlineHeight("small", _width - _imgsetting.width);
                    _totalarea += _hllength * (_width - _img.width) + _imgsetting.width * _imgsetting.height;
                    if (_totalarea <= _height * _width)
                    {
                        _b.headlinecaption = "small";
                        _b.headlinewidth = (_width - _imgsetting.width);
                        _b.headlinelength = _hllength;
                        _img.width = _imgsetting.width;
                        _img.length = _imgsetting.height;
                        ImageCaption _imgcaption = (ImageCaption)lstImageCaption[_img.id + "/" + _box.Id];
                        _img.captionlength = _imgcaption.getlines(_imgsetting.width) + ModelSettings.extraimagecaptionline;
                        _b.usedImageList.Add(_img);
                        _articlegenerated = true;
                        break;
                    }
                }
            }

            //POK: We need to generate article without image
            if (!_articlegenerated)
            {
                _b.headlinecaption = "small";
                _b.headlinewidth = _width;
                _b.headlinelength = _headline.GetHeadlineHeight("small", _width);
            }
            _boxlist.Add(_b);
        }

        if (_articletype == "briefs")
        {
            Box briefBox = Helper.DeepCloneBox(_box);
            briefBox.width = ModelSettings.briefSettings.width;
            briefBox.length = _box.origArea / briefBox.width;
            briefBox.volume = briefBox.length * briefBox.width;
            _boxlist.Add(briefBox);
        }

        if (_articletype == "letters")
        {
            Headline _headline = (Headline)headlines[_box.Id];

            //letter article sizes without image and all headline sizes provided in model tuning
            foreach (var headlineSize in ModelSettings.letterSettings.headlineSizes)
            {
                Box letterBox = Helper.DeepCloneBox(_box);

                letterBox.usedImageList = new List<Image>();

                letterBox.width = ModelSettings.canvaswidth;
                letterBox.length = (int)Math.Ceiling(_box.origArea / letterBox.width);

                letterBox.headlinecaption = headlineSize.ToLower();
                letterBox.headlinewidth = (int)letterBox.width;
                letterBox.headlinelength = _headline.GetHeadlineHeight(headlineSize.ToLower(), letterBox.headlinewidth);

                letterBox.length += letterBox.headlinelength;
                letterBox.volume = letterBox.length * letterBox.width;

                _boxlist.Add(letterBox);
            }

            //for now, we only consider the first image in the list
            if (_box.imageList.Count > 0)
            {
                Image letterImage = Helper.DeepCloneImage(_box.imageList.First());
                letterImage.width = ModelSettings.letterSettings.imageSize;
                var baseImageLength = Image.GetHeightUsingWidthHeightMap(letterImage.origlength, letterImage.origwidth, ModelSettings.letterSettings.imageSize);
                letterImage.length = baseImageLength;

                ImageCaption letterImageCaption = (ImageCaption)lstImageCaption[letterImage.id + "/" + _box.Id];
                var letterImageCaptionLength = letterImageCaption.getlines(letterImage.width) + ModelSettings.extraimagecaptionline;
                letterImage.captionlength = letterImageCaptionLength;

                letterImage.length += letterImage.captionlength;
                //letterImage.area = letterImage.width * letterImage.length;

                //image with no cropping combined with all headline sizes
                var letterBoxesWithoutImageCropping = _boxlist.Select(x =>
                {
                    var letterBox = Helper.DeepCloneBox(x);
                    letterBox.usedImageList = new List<Image> { Helper.DeepCloneImage(letterImage) };
                    letterBox.length = (int)Math.Ceiling((x.volume + letterImage.area) / letterBox.width);
                    letterBox.volume = letterBox.length * letterBox.width;
                    return letterBox;
                }).ToList();

                if (ModelSettings.bCropAllowed)
                {
                    var croppedImages = new List<Image>();
                    var cropPercentage = ModelSettings.croppercentage;
                    for (int j = 1; j <= Math.Round(baseImageLength * cropPercentage); j++)
                    {
                        Image clonedLetterImage = Helper.DeepCloneImage(letterImage);
                        clonedLetterImage.cropped = true;
                        clonedLetterImage.length = baseImageLength - j;
                        clonedLetterImage.length = clonedLetterImage.length + letterImageCaptionLength;
                        clonedLetterImage.captionlength = letterImageCaptionLength;
                        clonedLetterImage.croppercentage = (double)j * 100 / baseImageLength;
                        //clonedLetterImage.area = clonedLetterImage.width * clonedLetterImage.length;
                        croppedImages.Add(clonedLetterImage);
                    }

                    for (int j = 1; j <= Math.Round(baseImageLength * cropPercentage * letterImage.origwidth / letterImage.origlength); j++)
                    {
                        Image clonedLetterImage = Helper.DeepCloneImage(letterImage);
                        clonedLetterImage.cropped = true;
                        clonedLetterImage.length = baseImageLength + j;
                        clonedLetterImage.length = clonedLetterImage.length + letterImageCaptionLength;
                        clonedLetterImage.captionlength = letterImageCaptionLength;
                        clonedLetterImage.croppercentage = (double)j * 100 / baseImageLength;
                        //clonedLetterImage.area = clonedLetterImage.width * clonedLetterImage.length;
                        croppedImages.Add(clonedLetterImage);
                    }

                    //image with cropping combined with all headline sizes
                    var letterBoxesWithImageCropping = _boxlist.Select(x =>
                    {
                        return croppedImages.Select(croppedImage =>
                        {
                            var letterBox = Helper.DeepCloneBox(x);
                            letterBox.usedImageList = new List<Image> { Helper.DeepCloneImage(croppedImage) };
                            letterBox.length = (int)Math.Ceiling((x.volume + croppedImage.area) / letterBox.width);
                            letterBox.volume = letterBox.length * letterBox.width;
                            return letterBox;
                        });
                    }).SelectMany(x => x).ToList();

                    _boxlist.AddRange(letterBoxesWithImageCropping);
                }

                _boxlist.AddRange(letterBoxesWithoutImageCropping);

                //if we have an image, it will become mandatory, hence I am removing article sizes without image
                _boxlist.RemoveAll(x => x.usedImageList == null || x.usedImageList.Count == 0);
            }
        }

        if (_articletype.ToLower() == "picturelead")
        {
            if (_box.imageList != null && _box.imageList.Count > 0)
            {
                //abovehedline
                ModelSettings.PictureLeadArticleType aboveHeadline = ModelSettings.pictureLeadArticleTypes.Where(x => x.type == "aboveheadline").ToList().First();
                if (aboveHeadline != null)
                {
                    List<Box> _boxes = BuildPictureLeadAboveHeadline(_box, aboveHeadline);
                    if (_boxes != null)
                        _boxlist.AddRange(_boxes);
                }
                ModelSettings.PictureLeadArticleType partialHeadline = ModelSettings.pictureLeadArticleTypes.Where(x => x.type == "partialheadline").ToList().First();
                if (partialHeadline != null)
                {
                    List<Box> _boxes = BuildPictureLeadPartialHeadline(_box, partialHeadline);
                    if (_boxes != null)
                        _boxlist.AddRange(_boxes);
                }

            }

        }

        return _boxlist;
    }

    private List<Box> FindAllArticlePermutations(Box _box, int _noimagepermutation)
    {
        List<Box> _boxlist = new List<Box>();
        if (_box.Id == "7412ecdd-a644-49b3-b8c8-a614b05f6a9a")
            Log.Debug("Test");

        if (_box.articletype == "loft")
        {
            return FindArticleSizeForArticleTypes(_box, "loft");
        }

        if (_box.articletype.ToLower() == "briefs")
        {
            return FindArticleSizeForArticleTypes(_box, "briefs");
        }

        if (_box.articletype.ToLower() == "letters")
        {
            return FindArticleSizeForArticleTypes(_box, "letters");
        }
        if (_box.articletype.ToLower() == "picturelead")
        {
            List<Box> _sizes = FindArticleSizeForArticleTypes(_box, "picturelead");
            if (_sizes.Count > 0)
                return _sizes;
            else
                _box.articletype = "";
        }

        BoxSettings _settings = (BoxSettings)ModelSettings.boxsettings[_box.priority];
        int _minimgused = _settings.minimages;
        int _maximgused = _settings.maximages;
        int _minfctused = _settings.minfactsAvailable;

        int _fctcount = _box.factList == null ? 0 : _box.factList.Count;
        bool hasMugshots = (_box.imageList == null) ? false : _box.imageList.Any(x => x.imagetype == "mugshot");

        if (_fctcount > 1 && hasMugshots)
        {
            //Multifact, remove mugshot images.! Not supported mugshots with multifacts
            _box.imageList.RemoveAll(x => x.imagetype == "mugshot");
        }

        int _imagecount = _box.imageList == null ? 0 : _box.imageList.Count;
        int _citationcount = _box.citationList == null ? 0 : _box.citationList.Count;

        if ((_minimgused == 0 && _minfctused == 0) || (_imagecount == 0 && _fctcount == 0) || _noimagepermutation == 1 || _box.allowoverset)
            _boxlist = FindAllSizes(_box);

        if (_box.allowoverset)
            FindAllOversetSizes(_box, _boxlist);

        List<Image> _tempcitationList = null;
        List<Image> _tempfctList = null;
        if (_citationcount > 0)
            _tempcitationList = Image.GetAllPossibleImageSizes(_box.citationList[0], _box, 0);
        if (_fctcount > 0)
            _tempfctList = Image.GetAllPossibleImageSizes(_box.factList[0], _box, 0);

        List<Image> _tempimagelist = null;
        if (_imagecount > 0)
            _tempimagelist = Image.GetAllPossibleImageSizes(_box.imageList[0], _box, 1);
        /*Logic for MaxImage == 1*/
        if (_fctcount <= 1)
        {
            if (_imagecount == 0 || _minimgused == 0)
                GetArticleCombinationforNoImage(_box, _boxlist, _tempfctList, _tempcitationList, _minfctused);

            if (_maximgused == 1 && _imagecount >= 1)
            {
                GetArticleCombinationforOneImage(_box, _boxlist, _tempimagelist, _tempfctList, _tempcitationList, _minfctused);
            }

            if (_maximgused == 2 && _imagecount >= 1)
            {
                //2 options possible, either choose 1 or 2 images
                //with 1 image
                GetArticleCombinationforOneImage(_box, _boxlist, _tempimagelist, _tempfctList, _tempcitationList, _minfctused);

                //With 2 images
                if (_imagecount >= 2)
                {
                    List<Image> _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);
                    GetArticleCombinationforTwoImages(_box, _boxlist, _tempimagelist, _tempsubimageList, _tempfctList, _tempcitationList, _minfctused);
                }
            }

            if (_maximgused >= 3 && _imagecount >= 1)
            {
                //2 options possible, either choose 1 or 2 images
                //with 1 image
                GetArticleCombinationforOneImage(_box, _boxlist, _tempimagelist, _tempfctList, _tempcitationList, _minfctused);

                //With 2 images
                if (_imagecount >= 2)
                {
                    List<Image> _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);
                    GetArticleCombinationforTwoImages(_box, _boxlist, _tempimagelist, _tempsubimageList, _tempfctList, _tempcitationList, _minfctused);
                }
                //With 2 images
                if (_imagecount >= 3)
                {
                    List<Image> _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);
                    List<Image> _tempsubimageList2 = Image.GetAllPossibleImageSizes(_box.imageList[2], _box, 0);
                    GetArticleCombinationforThreeImages(_box, _boxlist, _tempimagelist, _tempsubimageList, _tempsubimageList2, _tempfctList, _tempcitationList, _minfctused);

                    try
                    {
                        if (ModelSettings.enableNewLayoutsBelowHeadline && !hasMugshots)
                        {
                            var tempLayouts = LayoutGenerator.GenerateThreeImageLayouts(_box, _tempimagelist, _tempsubimageList, _tempsubimageList2, _tempfctList, _tempcitationList);
                            _boxlist.AddRange(tempLayouts);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("Error while generating BelowHeadline layouts: {msg}", e.StackTrace);
                    }
                }

                if (_maximgused >= 4 && _imagecount >= 4)
                {
                    List<Image> _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);
                    List<Image> _tempsubimageList2 = Image.GetAllPossibleImageSizes(_box.imageList[2], _box, 0);
                    List<Image> _tempsubimageList3 = Image.GetAllPossibleImageSizes(_box.imageList[3], _box, 0);
                    List<List<Image>> lstlastImage = new List<List<Image>>() { _tempsubimageList, _tempsubimageList2, _tempsubimageList3 };
                    GetArticleCombinationforNImages(_box, _boxlist, _tempimagelist, lstlastImage, _tempfctList, _tempcitationList, _minfctused);

                    try
                    {
                        if (ModelSettings.enableNewLayoutsBelowHeadline && !hasMugshots)
                        {
                            var tempLayouts = LayoutGenerator.GenerateFourImageLayouts(_box, _tempimagelist, _tempsubimageList, _tempsubimageList2, _tempsubimageList3, _tempfctList, _tempcitationList);
                            _boxlist.AddRange(tempLayouts);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("Error while generating BelowHeadline layouts: {msg}", e.StackTrace);
                    }
                }

                if (_maximgused >= 5 && _imagecount >= 5)
                {
                    List<Image> _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);
                    List<Image> _tempsubimageList2 = Image.GetAllPossibleImageSizes(_box.imageList[2], _box, 0);
                    List<Image> _tempsubimageList3 = Image.GetAllPossibleImageSizes(_box.imageList[3], _box, 0);
                    List<Image> _tempsubimageList4 = Image.GetAllPossibleImageSizes(_box.imageList[4], _box, 0);

                    try
                    {
                        if (!hasMugshots)
                        {
                            var tempLayouts = LayoutGenerator.GenerateFiveImageLayouts(_box, _tempimagelist, _tempsubimageList, _tempsubimageList2, _tempsubimageList3, _tempsubimageList4, _tempfctList, _tempcitationList);
                            _boxlist.AddRange(tempLayouts);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("Error while generating BelowHeadline layouts: {msg}", e.StackTrace);
                    }
                }

            }
        }
        else //multiple facts
        {
            List<Image> _tempsubimageList = null;
            if (_imagecount >= 2)
                _tempsubimageList = Image.GetAllPossibleImageSizes(_box.imageList[1], _box, 0);

            List<List<Image>> _lstlstFact = new List<List<Image>>();
            List<List<Image>> _lstlstImage = new List<List<Image>>();
            foreach (var _fact in _box.factList)
                _lstlstFact.Add(Image.GetAllPossibleImageSizes(_fact, _box, 0));

            for (int _j = 1; _j < _box.imageList.Count(); _j++)
                _lstlstImage.Add(Image.GetAllPossibleImageSizes(_box.imageList[_j], _box, 0));

            GetArticleCombinationforMultiFact(_box, _boxlist, _tempimagelist, _tempsubimageList, Helper.CrossJoin(_lstlstFact), _tempcitationList);

            if (ModelSettings.morelayoutsformultifacts)
            {
                MultiFactHandler.GenerateAllArticleCombinationforMultiFact(_box, _boxlist, _tempimagelist, _tempsubimageList, Helper.CrossJoin(_lstlstFact), _tempcitationList);
            }
        }
        var belowheadlineLayouts = _boxlist.Where(x => !string.IsNullOrWhiteSpace(x.layout)).ToList();

        //set the layout to aboveheadline
        _boxlist.Where(x => string.IsNullOrWhiteSpace(x.layout)).ToList().ForEach(x => x.layouttype = LayoutType.aboveheadline);

        if (ModelSettings.morelayoutsformultifacts)
        {
            List<Box> preferredMultiFactLayout = _boxlist.Where(x => x.multiFactStackingOrder != "")
                .Select((box, index) => new { box, index })
                .GroupBy(b => new { b.box.width, b.box.length, b.box.usedimagecount })
                .Select(g => g.OrderBy(b => ModelSettings.multiFactStackingOrderPreference.IndexOf(b.box.multiFactStackingOrder) >= 0
                            ? ModelSettings.multiFactStackingOrderPreference.IndexOf(b.box.multiFactStackingOrder)
                            : int.MaxValue)
                .First().box)
                .ToList();

            _boxlist.RemoveAll(x => !string.IsNullOrEmpty(x.multiFactStackingOrder) && !preferredMultiFactLayout.Contains(x));

        }

        int _i = 1;
        if (ModelSettings.haskicker == 1)
        {
            foreach (var _tempbox in _boxlist)
            {
                if (kickersmap.Count > 0 && (Kicker)kickersmap[_tempbox.Id] != null)
                {
                    int kickerlength = (int)((Kicker)kickersmap[_tempbox.Id]).collinemap[(int)_tempbox.width];
                    _tempbox.kickerlength = kickerlength;
                    _tempbox.length = _tempbox.length + kickerlength;
                }
                _i++;
            }
        }

        List<Box> newBoxList = new List<Box>();
        Headline _headline = (Headline)headlines[_box.Id];
        //Add small headline
        List<string> Hlsizes = new List<string>() { "large", "medium", "small" };
        foreach (Box _tbox in _boxlist)
        {
            if (!_tbox.isjumparticle)
            {
                if (_headline.collinemap.Count==0 && _headline.mediumcollinemap.Count==0 && _headline.largecollinemap.Count==0)
                {
                    if (_tbox.length <= canvasz - ModelSettings.sectionheadheight)
                    {
                        Box _tempbox = Helper.DeepCloneBox(_tbox);
                        _tempbox.headlinecaption = _settings.headlinetype;
                        _tempbox.headlinelength = 0;
                        _tempbox.headlinewidth = 0;
                        newBoxList.Add(_tempbox);
                    }
                }
                int _prevhlheight = 0;
                foreach (var _hlsize in Hlsizes)
                {
                    if (!ModelSettings.enableLargeHeadlinesForLowPriority)
                    {
                        if (_hlsize == "large" && _tbox.priority <= 3)
                            continue;
                    }

                    int _hlheight = _headline.GetHeadlineHeight(_hlsize, (int)_tbox.width);
                    int _hltypeline = _headline.GetHeadlineTypoLines(_hlsize, (int)_tbox.width);
                    if (_hlheight <= 0) continue;
                    if (_hlheight == _prevhlheight) continue;

                    if (_tbox.length + _hlheight > canvasz - ModelSettings.sectionheadheight)
                        continue;

                    _prevhlheight = _hlheight;
                    Box _tempbox = Helper.CustomCloneBox(_tbox);
                    _tempbox.headlinecaption = _hlsize;
                    _tempbox.headlinelength = _hlheight;
                    _tempbox.headlinetypoline = _hltypeline;
                    _tempbox.length += _hlheight;
                    _tempbox.headlinewidth = (int)_tempbox.width;
                    newBoxList.Add(_tempbox);

                }

            }
            else
            {
                Jumps _jumps = dictJumpSettings[_box.Id];
                JumpLine _jumpheadline = _jumps.lstJumpHeadline.Find(x => x.column == _tbox.width);
                int _hlsize = _jumpheadline.lines;
                int _hltypeline = _jumpheadline.typolines;
                if (_hlsize > 0)
                {
                    _hlsize += ModelSettings.extraheadlineline;
                    Box _tempbox = Helper.DeepCloneBox(_tbox);
                    _tempbox.headlinecaption = _settings.headlinetype;
                    _tempbox.headlinelength = _hlsize;
                    _tempbox.headlinetypoline = _hltypeline;
                    _tempbox.length += _hlsize;
                    _tempbox.headlinewidth = (int)_tempbox.width;
                    if (_tempbox.length <= canvasz - ModelSettings.sectionheadheight)
                        newBoxList.Add(_tempbox);
                }
                else
                {
                    if (_tbox.length <= canvasz - ModelSettings.sectionheadheight)
                    {
                        Box _tempbox = Helper.DeepCloneBox(_tbox);
                        _tempbox.headlinecaption = _settings.headlinetype;
                        _tempbox.headlinelength = 0;
                        _tempbox.headlinewidth = 0;
                        newBoxList.Add(_tempbox);
                    }
                }
            }
        }


        _i = 1;
        foreach (var _tempbox in newBoxList)
        {
            _tempbox.volume = _tempbox.length * _tempbox.width;
            _tempbox.usedimagecount = _tempbox.usedImageList == null ? 0 : _tempbox.usedImageList.Count();

            _tempbox.layouttype = LayoutType.belowheadline;

            if (_tempbox.usedImageList != null)
            {
                _tempbox.usedaboveimagecount = _tempbox.usedImageList.Where(x => x.aboveHeadline == true).Count();

                if (_tempbox.usedaboveimagecount > 0)
                    _tempbox.layouttype = LayoutType.aboveheadline;
            }
            _tempbox.runid = _i;
            _i++;
        }


        return newBoxList;
    }

    private List<Box> FindSizesForDoubleTruck(Box _box, List<Image> _fctlist)
    {
        List<Box> _sizes = new List<Box>();
        Image _tmpfct = null;
        int bvalidateforsquare = 0;
        double _area;
        int _minfctlength = 0;

        _area = _box.origArea;
        if (_fctlist != null)
        {
            foreach (var _fct in _fctlist)
            {
                List<Image> _lstImage = Image.GetAllPossibleImageSizes(_fct, _box, 0);
                _tmpfct = _lstImage.OrderByDescending(x => -1 * x.width * x.length).ToList()[0];
                _area = _area + _tmpfct.length * _tmpfct.width;
                _minfctlength = _lstImage.OrderByDescending(x => -1 * x.length).ToList()[0].length;
            }
        }


        for (int _width = ModelSettings.clsDoubleTruck.mainarticleminsize; _width <= ModelSettings.clsDoubleTruck.mainarticlemaxsize; _width++)
        {

            int _length = (int)Math.Ceiling(_area / _width);
            if (_length <= canvasz)
            {
                if (_length < _box.preamble)
                    _length = _box.preamble;

                if (_length < _minfctlength)
                    _length = _minfctlength;

                Box _newbox = Helper.DeepCloneBox(_box);
                _newbox.length = _length;
                _newbox.width = _width;
                _newbox.volume = _length * _width;

                _newbox.usedImageList = new List<Image>();
                if (_length == _width)
                    bvalidateforsquare = 1;

                if (_tmpfct != null)
                {
                    _newbox.usedImageList.Add(_tmpfct);
                }
                _sizes.Add(_newbox);
            }
        }
        return _sizes;
    }

    private List<Box> FindAllSizes(Box _box)
    {
        List<Box> _sizes = new List<Box>();
        int bvalidateforsquare = 0;
        //int minWidth = 1;
        //int maxWidth = canvasx;
        double _area;

        _area = _box.origArea;
        //minWidth = _box.mincolumns();
        //maxWidth = _box.maxcolumns();

        //for (int _width = minWidth; _width <= maxWidth; _width++)
        foreach (int _width in _box.avalableLengths())
        {

            int _length = (int)Math.Ceiling(_area / _width);
            if (_length <= canvasz || _box.allowoverset)
            {
                if (_length < _box.preamble + _box.byline)
                    continue;

                if (_length == _width && bvalidateforsquare == 1)
                    continue;

                Box _newbox = Helper.DeepCloneBox(_box);
                _newbox.length = _length;
                _newbox.width = _width;
                _newbox.volume = _length * _width;


                if (_length == _width)
                    bvalidateforsquare = 1;

                _sizes.Add(_newbox);
            }
        }

        //Couldn't find the size because of preamble min length, add default size
        if (_sizes.Count() == 0)
        {
            //for (int _width = minWidth; _width <= maxWidth; _width++)
            foreach (int _width in _box.avalableLengths())
            {
                Box _newbox = Helper.DeepCloneBox(_box);
                int _length = (int)Math.Ceiling(_area / _width);
                if (_box.preamble > 0 && _length <= canvasz)
                {
                    _newbox.length = _box.preamble;
                    _newbox.width = _width;
                    _newbox.volume = _box.preamble * _width;

                    _sizes.Add(_newbox);
                }
            }
        }
        return _sizes;
    }

    private int GetMaxlengthOfSubImages(List<Image> _subimages)
    {
        int _len = 0;
        foreach (Image _subimage in _subimages)
        {
            if (_subimage != null)
                if (_subimage.length > _len)
                    _len = _subimage.length;
        }
        return _len;
    }

    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, List<Image> _fctlist, Image _tcitImage)
    {
        List<Box> _sizes = new List<Box>();

        _sizes = FindAllArticleSizes(_box, _tmainImg, _fctlist, _tcitImage, null, 0);
        //Try to find the fit by adding whitepaces
        int _totalsizes = _box.avalableLengths().Count();
        int _sizesinlayouts = _sizes.Count();
        int _startwhilelines = ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        while (ModelSettings.clsWhiteSpaceSettings.addwhitespacelines && _sizesinlayouts < _totalsizes
            && _startwhilelines <= ModelSettings.clsWhiteSpaceSettings.maxwhitespacelines)
        {
            foreach (var _vsize in _box.avalableLengths())
            {
                if (_sizes.Where(x => x.width == _vsize).Count() == 0)
                {
                    Box _newbox = Helper.CustomCloneBox(_box);
                    _newbox.origArea += _startwhilelines;
                    _newbox.whitespace = _startwhilelines;
                    List<Box> _whitespaceboxes = FindAllArticleSizes(_newbox, _tmainImg, _fctlist, _tcitImage, null, _vsize);

                    if (_whitespaceboxes != null && _whitespaceboxes.Count > 0)
                    {
                        _sizes.AddRange(_whitespaceboxes);
                    }
                }
            }
            _sizesinlayouts = _sizes.Count();
            _startwhilelines += ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        }
        return _sizes;
    }
    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, List<Image> _fctlist, Image _tcitImage, List<Image> mugshots, int _boxwidth)
    {
        List<Box> _sizes = new List<Box>();
        double _area = 0;
        Image _fctImage = null, _mainImg, _citImage = null;

        if (_tmainImg.imagetype == "mugshot")
        {
            return FindAllArticleSizesNoMain(_box, _fctlist, _tcitImage, new List<Image> { _tmainImg });
        }

        if (mugshots != null)
            mugshots = mugshots.Take(ModelSettings.mugshotSetting.maxMugshotAllowed).ToList();

        int _ipreamble = 1;
        _ipreamble += (mugshots != null) ? mugshots.Count : 0;
        var mugshotArea = (mugshots != null) ? mugshots.Sum(x => x.imageMetadata.doubleSize.Area) : 0;

        foreach (int _width in _box.avalableLengths())
        {
            if (_boxwidth > 0 && _width != _boxwidth)
                continue;
            _mainImg = Helper.CustomCloneImage(_tmainImg);
            if (_fctlist != null && _fctlist.Count() > 0)
            {
                _fctImage = Helper.CustomCloneImage(_fctlist[0]);
            }
            if (_tcitImage != null)
                _citImage = Helper.CustomCloneImage(_tcitImage);

            if ((_fctImage != null && _fctImage.width == _width))
            {
                List<Box> _lst = GenerateSizeForImageWidthSameAsArticleWidth(_box, _mainImg, new List<Image>() { }, _fctlist, _citImage, mugshots, _width);
                if (_lst != null)
                    _sizes.AddRange(_lst);
                continue;
            }

            double _iarea1 = 0, _iarea2 = 0, _iarea3 = 0, _newboxarea;

            _area = _box.origArea;
            _iarea1 = _mainImg.length * _mainImg.width;
            if (_fctImage != null)
                _iarea2 = _fctImage.length * _fctImage.width;
            if (_citImage != null)
                _iarea3 = _citImage.length * _citImage.width;
            _newboxarea = _area + _iarea1 + _iarea2 + _iarea3 + mugshotArea;

            List<Image> _subimages = new List<Image>
                {
                    null,
                    _fctImage,
                    _citImage
                };
            int _maxsublength = GetMaxlengthOfSubImages(_subimages);

            //Width of the final article cannot be less than the image width
            if (_width < _mainImg.width)
                continue;
            if (_fctImage != null && (_width - _ipreamble < _fctImage.width || !Helper.isValidFact(_fctImage)))
                continue;
            if (_citImage != null && _width - _ipreamble < _citImage.width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);
            int _remaininglength = _length;

            if (_length < _box.preamble + _box.byline)
                continue;
            if (_length < _mainImg.length)
                continue;
            //POK: Main image will be placed above the header
            if (_mainImg.width == _width)
            {
                //Length of the remianing box > preamble
                if (_length - _mainImg.length < _box.preamble + _box.byline)
                    continue;

                if (_fctImage != null || _citImage != null)
                {
                    if (!CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, _subimages))
                        continue;
                }
                _mainImg.aboveHeadline = true;
                _remaininglength = _length - _mainImg.length;
            }
            else if (_mainImg.width < _width)
            {
                bool cantproceed = true;
                if (_fctImage != null)
                {
                    if (_mainImg.width + _fctImage.width == _width)
                    {
                        if (_fctImage.length <= _mainImg.length && _fctImage.length >= _mainImg.length - _mainImg.captionlength)
                        {
                            //check if citation can be placed
                            if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { null, null, _citImage }))
                            {
                                cantproceed = false;
                                _mainImg.aboveHeadline = true;
                                _fctImage.aboveHeadline = true;
                                _remaininglength = _length - _mainImg.length;
                            }
                        }
                    }
                }

                if (cantproceed)
                {
                    List<Image> _templist = new List<Image>() { _mainImg, _fctImage, _citImage };
                    if (cantproceed && CanImagesBeFittedInsideBox(_box, _width, _length, _ipreamble, _templist))
                        cantproceed = false;
                }
                if (cantproceed)
                    continue;
            }
            //Length of the remianing box > preamble
            //EPSLN-42
            if (_mainImg.aboveHeadline && (_length - _mainImg.length < _box.preamble + _box.byline))
                continue;

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.usedImageList = new List<Image>();

            _newbox.usedImageList.Add(_mainImg);
            if (_fctImage != null)
                _newbox.usedImageList.Add(_fctImage);
            if (_citImage != null)
                _newbox.usedImageList.Add(_citImage);

            if (mugshots != null)
                _newbox.usedImageList.AddRange(mugshots);

            if (Helper.isValidBox(_newbox, _ipreamble))
            {
                _sizes.Add(_newbox);
            }
        }

        return _sizes;
    }
    public List<Box> FindAllArticleSizesNoMain(Box _box, List<Image> _fctlist, Image _tcitImage, List<Image> mugshots)
    {
        List<Box> _sizes = new List<Box>();
        double _area = 0;
        Image _fctImage = null, _mainImg, _citImage = null;

        if (mugshots != null)
            mugshots = mugshots.Take(ModelSettings.mugshotSetting.maxMugshotAllowed).ToList();

        int _ipreamble = _box.preamble > 0 ? 1 : (mugshots?.Count > 0) ? 1 : 0;
        _ipreamble += (mugshots != null) ? mugshots.Count : 0;
        var mugshotArea = (mugshots != null) ? mugshots.Sum(x => x.imageMetadata.doubleSize.Area) : 0;

        foreach (int _width in _box.avalableLengths())
        {
            if (_fctlist != null && _fctlist.Count() > 0)
            {
                _fctImage = Helper.DeepCloneImage(_fctlist[0]);
            }
            if (_tcitImage != null)
                _citImage = Helper.DeepCloneImage(_tcitImage);

            double _iarea2 = 0, _iarea3 = 0, _newboxarea;

            _area = _box.origArea;
            if (_fctImage != null)
                _iarea2 = _fctImage.length * _fctImage.width;
            if (_citImage != null)
                _iarea3 = _citImage.length * _citImage.width;
            _newboxarea = _area + _iarea2 + _iarea3 + mugshotArea;

            if (_newboxarea <= 0)
                continue;
            List<Image> _subimages = new List<Image>
                {
                    null,
                    _fctImage,
                    _citImage
                };
            int _maxsublength = GetMaxlengthOfSubImages(_subimages);

            //Width of the final article cannot be same or less than fact/Cit
            if (_fctImage != null && (_width <= _fctImage.width || !Helper.isValidFact(_fctImage)))
                continue;
            if (_citImage != null && _width <= _citImage.width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);
            int _remaininglength = _length;

            if (_length < _box.preamble + _box.byline)
                continue;

            if (_fctImage != null || _citImage != null)
            {
                if (!CanImagesBeFittedInsideBox(_box, _width, _length, _ipreamble, _subimages))
                    continue;
            }

            Box _newbox = Helper.DeepCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.usedImageList = new List<Image>();
            if (_fctImage != null)
                _newbox.usedImageList.Add(_fctImage);
            if (_citImage != null)
                _newbox.usedImageList.Add(_citImage);

            if (mugshots != null)
                _newbox.usedImageList.AddRange(mugshots);

            if (Helper.isValidBox(_newbox, _ipreamble))
            {
                _sizes.Add(_newbox);
            }
        }

        return _sizes;
    }

    private bool CheckImageInvalidLengths(List<Image> _images, int _remlength)
    {
        bool _invalid = false;
        foreach (Image _img in _images)
        {
            if (_img == null) continue;
            if (_img.length > _remlength - ModelSettings.minimumlinesunderImage && _img.length < _remlength)
                _invalid = true;
            if (_img.length > _remlength)
                _invalid = true;
        }

        return _invalid;
    }

    private bool CheckImageInvalidLengths(List<Image> _images, int _remlength, int _whitespace)
    {
        bool _invalid = false;
        int _extralines = 0;
        _images.RemoveAll(x => x == null);
        foreach (Image _img in _images)
        {
            if (_img == null) continue;
            if (_img.length > _remlength)
                _invalid = true;
        }
        if (!_invalid)
        {
            foreach (Image _img in _images.Where(x => x.length > _remlength - ModelSettings.minimumlinesunderImage && x.length < _remlength))
            {
                _extralines += (_remlength - _img.length) * _img.width;
            }
            if (_extralines != 0 && _extralines > _whitespace)
                _invalid = true;
            if (_extralines != 0 && _extralines <= _whitespace)
            {
                foreach (Image _img in _images.Where(x => x.length > _remlength - ModelSettings.minimumlinesunderImage && x.length < _remlength))
                {
                    _img.length = (_remlength);
                }
            }
        }

        return _invalid;
    }
    private bool CanImagesBeFittedInsideBox(Box _box, int _width, int _remlength, int _ipreamble, List<Image> _subimages)
    {
        Image _subImg = _subimages[0];
        Image _fctImage = _subimages[1];
        Image _citImage = _subimages[2];

        bool canbefitted = false;


        if (_subImg == null && _fctImage == null && _citImage == null)
        {
            return true;
        }


        if (_subImg != null)
        {
            if (_subImg.length > _remlength - ModelSettings.minimumlinesunderImage && _subImg.length < _remlength)
                return false;
            if (_subImg.length > _remlength)
                return false;
            if (_box.priority == 5 && _subImg.width > _width - _ipreamble)
                return false;
            else if (_subImg.width > _width)
                return false;
        }
        if (_fctImage != null)
        {
            if (_fctImage.length > _remlength - ModelSettings.minimumlinesunderImage && _fctImage.length < _remlength)
                return false;
            if (_fctImage.length > _remlength)
                return false;
            if (_fctImage.width > _width - _ipreamble)
                return false;
        }
        if (_citImage != null)
        {
            if (_citImage.length > _remlength - ModelSettings.minimumlinesunderImage && _citImage.length < _remlength)
                return false;
            if (_citImage.length > _remlength)
                return false;
            if (_citImage.width > _width - _ipreamble)
                return false;
        }

        if (_subImg != null)
        {
            if (_fctImage != null)
            {
                if (_citImage is null)
                {
                    if (_box.priority < 5 && _subImg.width == _width && _remlength >= _subImg.length + ((int)Math.Ceiling(_box.origArea / _width)))
                    {
                        _fctImage.topimageinsidearticle = true;
                        _subImg.relativex = 0;
                        _subImg.relativey = -_subImg.length;
                        canbefitted = true;
                    }
                    else if ((_width - _ipreamble >= _fctImage.width + _subImg.width))
                    {
                        _subImg.topimageinsidearticle = true;
                        _fctImage.topimageinsidearticle = true;
                        canbefitted = true;
                    }
                    else if (_fctImage.width <= _subImg.width && (_fctImage.length + _subImg.length == _remlength ||
                        _fctImage.length + _subImg.length <= _remlength - ModelSettings.minimumlinesunderImage))
                    {
                        _subImg.topimageinsidearticle = true;
                        canbefitted = true;
                    }
                }
                else
                {
                    _subImg.topimageinsidearticle = true;
                    //If all the subimage,fact &cit are not null,we need to set the relativex
                    if (_width - _ipreamble == _subImg.width)
                    {
                        int _maxlen = _subImg.length + (_fctImage.length > _citImage.length ? _fctImage.length : _citImage.length);
                        if (_citImage.width + _fctImage.width > _subImg.width)
                            return false;
                        if (_maxlen == _remlength || _maxlen <= _remlength - ModelSettings.minimumlinesunderImage)
                        {
                            _subImg.relativex = _width - _subImg.width;
                            _subImg.relativey = 0;
                            if (_maxlen == _remlength)
                            {
                                if (_fctImage.length == _citImage.length)
                                {
                                    _fctImage.relativex = _width - _fctImage.width;
                                    _fctImage.relativey = _subImg.length;
                                    _citImage.relativex = _width - _fctImage.width - _citImage.width;
                                    _citImage.relativey = _subImg.length;
                                }
                                else if (_fctImage.length > _citImage.length)
                                {
                                    _fctImage.relativex = _width - _fctImage.width;
                                    _fctImage.relativey = _subImg.length;
                                    _citImage.relativex = _subImg.relativex;
                                    _citImage.relativey = _subImg.length;
                                }
                                else
                                {
                                    _citImage.relativex = _width - _citImage.width;
                                    _citImage.relativey = _subImg.length;
                                    _fctImage.relativex = _subImg.relativex;
                                    _fctImage.relativey = _subImg.length;
                                }
                            }
                            else
                            {
                                _fctImage.relativex = _width - _fctImage.width;
                                _fctImage.relativey = _subImg.length;
                                _citImage.relativex = _subImg.relativex;
                                _citImage.relativey = _subImg.length;
                            }
                            canbefitted = true;
                        }

                    }
                    if ((_width - _ipreamble >= _fctImage.width + _subImg.width))
                    {
                        _fctImage.topimageinsidearticle = true;
                        _subImg.topimageinsidearticle = true;
                        if (_citImage.width <= _subImg.width)
                        {
                            //Sub image +Cit should be placed in the center
                            if (_citImage.length + _subImg.length <= _remlength - ModelSettings.minimumlinesunderImage)
                            {
                                canbefitted = true;
                                _citImage.parentimageid = _subImg.id;
                                _subImg.relativex = _ipreamble;
                                _citImage.relativex = _ipreamble + (_subImg.width - _citImage.width) / 2;
                                _citImage.relativey = _subImg.length;
                                _fctImage.relativex = _width - _fctImage.width;
                            }
                            //Sub image +Cit should be placed to the right
                            else if (_citImage.length + _subImg.length == _remlength)
                            {
                                canbefitted = true;
                                _citImage.parentimageid = _subImg.id;
                                _subImg.relativex = _width - _subImg.width;
                                _citImage.relativex = _width - _citImage.width;
                                _citImage.relativey = _subImg.length;
                                _fctImage.relativex = _ipreamble;
                            }
                        }
                        if (_citImage.width <= _fctImage.width && !canbefitted)
                        {
                            //Sub image +Fact should be placed in the center
                            if (_citImage.length + _fctImage.length <= _remlength - ModelSettings.minimumlinesunderImage)
                            {
                                canbefitted = true;
                                _citImage.parentimageid = _fctImage.id;
                                _fctImage.relativex = _ipreamble;
                                _citImage.relativex = _ipreamble + (_fctImage.width - _citImage.width) / 2;
                                _citImage.relativey = _fctImage.length;
                                _subImg.relativex = _width - _subImg.width;
                            }
                            //Sub image +Fact should be placed to the right
                            else if (_citImage.length + _fctImage.length == _remlength)
                            {
                                canbefitted = true;
                                _citImage.parentimageid = _fctImage.id;
                                _fctImage.relativex = _width - _fctImage.width;
                                _citImage.relativex = _width - _citImage.width;
                                _citImage.relativey = _fctImage.length;
                                _subImg.relativex = _ipreamble;
                            }
                        }

                    }
                    if (!canbefitted && (_width - _ipreamble >= _citImage.width + _subImg.width) && _fctImage.width <= _subImg.width)
                    {
                        int _imgmaxlen = _subImg.length + _fctImage.length;
                        if (_imgmaxlen == _remlength)
                        {
                            _subImg.relativex = _width - _subImg.width;
                            _subImg.relativey = 0;
                            _fctImage.relativex = _width - _fctImage.width;
                            _fctImage.relativey = _subImg.length;
                            _citImage.relativex = _ipreamble;
                            _citImage.relativey = 0;
                            canbefitted = true;
                        }
                        else if (_imgmaxlen <= _remlength - ModelSettings.minimumlinesunderImage)
                        {
                            _subImg.relativex = _ipreamble;
                            _subImg.relativey = 0;
                            _fctImage.relativex = _ipreamble + (_subImg.width - _fctImage.width) / 2;
                            _fctImage.relativey = _subImg.length;
                            _citImage.relativex = _width - _citImage.width;
                            _citImage.relativey = 0;
                            canbefitted = true;
                        }
                    }
                }

            }
            else if (_citImage != null)
            {
                if (_box.priority < 5 && _subImg.width == _width && _remlength >= _subImg.length + ((int)Math.Ceiling(_box.origArea / _width)))
                {
                    _citImage.topimageinsidearticle = true;
                    _subImg.relativex = 0;
                    _subImg.relativey = -_subImg.length;
                    canbefitted = true;
                }
                else if ((_width - _ipreamble >= _citImage.width + _subImg.width))
                {
                    _subImg.topimageinsidearticle = true;
                    _citImage.topimageinsidearticle = true;
                    canbefitted = true;
                }
                else if (_citImage.width <= _subImg.width && (_citImage.length + _subImg.length == _remlength ||
                        _citImage.length + _subImg.length <= _remlength - ModelSettings.minimumlinesunderImage))
                {
                    _subImg.topimageinsidearticle = true;
                    canbefitted = true;
                }
            }
            else if (_box.priority < 5 && _subImg.width == _width && _remlength >= _subImg.length + ((int)Math.Ceiling(_box.origArea / _width)))
            {
                _subImg.relativex = 0;
                _subImg.relativey = -_subImg.length;
                canbefitted = true;
            }
            else if ((_subImg.length == _remlength ||
                        _subImg.length <= _remlength - ModelSettings.minimumlinesunderImage) && _subImg.width < _width)
            {
                _subImg.topimageinsidearticle = true;
                canbefitted = true;
            }
        }
        else
        {
            if (_fctImage != null)
            {
                if (_citImage is null)
                {
                    //if (_remlength > _fctImage.length)
                    if (_fctImage.length == _remlength || _fctImage.length <= _remlength - ModelSettings.minimumlinesunderImage)
                        canbefitted = true;
                }
                else
                {
                    int _imglen = GetMaxlengthOfSubImages(new List<Image>() { _citImage, _fctImage });
                    if ((_width - _ipreamble >= _citImage.width + _fctImage.width))
                    {
                        //POK: Not needed. this validation has  been added at the top
                        //if (_remlength > _citImage.length && _remlength > _fctImage.length)
                        {
                            _citImage.topimageinsidearticle = true;
                            _fctImage.topimageinsidearticle = true;
                            canbefitted = true;
                        }
                    }
                    //else if (_remlength > _citImage.length + _fctImage.length)
                    else if (_citImage.length + _fctImage.length == _remlength ||
                    _citImage.length + _fctImage.length <= _remlength - ModelSettings.minimumlinesunderImage)
                        canbefitted = true;
                }

            }
            else if (_citImage != null)
            {
                //Pok FLOW-279
                if ((_width - _ipreamble >= _citImage.width))
                    canbefitted = true;
            }

        }
        return canbefitted;
    }
    private bool CanImagesBeFittedInsideBox_V2(Box _box, int _width, int _remlength, int _ipreamble, List<Image> _subimages,
        int _mainstartx, int _mainstarty)
    {
        Image _subImg = _subimages[0];
        Image _fctImage = _subimages[1];
        Image _citImage = _subimages[2];

        bool canbefitted = false;
        if (_subImg == null && _fctImage == null && _citImage == null)
        {
            return true;
        }


        if (_subImg != null)
        {
            if (_subImg.length > _remlength - ModelSettings.minimumlinesunderImage && _subImg.length < _remlength)
                return false;
        }
        if (_fctImage != null)
        {
            if (_fctImage.length > _remlength - ModelSettings.minimumlinesunderImage && _fctImage.length < _remlength)
                return false;
        }
        if (_citImage != null)
        {
            if (_citImage.length > _remlength - ModelSettings.minimumlinesunderImage && _citImage.length < _remlength)
                return false;
        }


        if (_subImg != null)
        {
            _subImg.topimageinsidearticle = true;
            if (_fctImage != null)
            {
                if (_citImage is null)
                {
                    if ((_width - _ipreamble >= _fctImage.width + _subImg.width))
                    {
                        canbefitted = true;
                        _fctImage.topimageinsidearticle = true;
                        _subImg.relativey = _mainstarty;
                        _fctImage.relativey = _mainstarty;
                        if (_subImg.length == _remlength && _fctImage.length == _remlength)
                        {
                            _subImg.relativex = _mainstartx + _width - _subImg.width;
                            _fctImage.relativex = _mainstartx + _width - _subImg.width - _fctImage.width;
                        }
                        else if (_subImg.length == _remlength)
                        {
                            _subImg.relativex = _mainstartx + _width - _subImg.width;
                            _fctImage.relativex = _mainstartx + _width - _subImg.width - _fctImage.width - _ipreamble;
                        }
                        if (_fctImage.length == _remlength)
                        {
                            _fctImage.relativex = _mainstartx + _width - _fctImage.width;
                            _subImg.relativex = _mainstartx + _width - _subImg.width - _fctImage.width - _ipreamble;
                        }
                        else //DEFAULT
                        {
                            _subImg.relativex = _mainstartx + _width - _subImg.width;
                            _fctImage.relativex = _mainstartx + _width - _subImg.width - _fctImage.width - _ipreamble;
                        }
                    }
                    else if (_fctImage.length + _subImg.length <= _remlength - ModelSettings.minimumlinesunderImage)
                    {
                        canbefitted = true;
                        int _startx = _mainstartx + _width - _ipreamble - Math.Max(_subImg.width, _fctImage.width);
                        if (_subImg.width >= _fctImage.width)
                        {
                            _subImg.relativex = _startx;
                            _fctImage.relativex = _startx + (_subImg.width - _fctImage.width) / 2;
                            _subImg.relativey = _mainstarty;
                            _fctImage.relativey = _mainstarty + _subImg.length;
                        }
                        else
                        {
                            _fctImage.relativex = _startx;
                            _subImg.relativex = _startx + (_fctImage.width - _subImg.width) / 2;
                            _fctImage.relativey = _mainstarty;
                            _subImg.relativey = _mainstarty + _fctImage.length;
                        }
                    }
                    else if (_fctImage.length + _subImg.length == _remlength)
                    {
                        canbefitted = true;
                        _subImg.relativex = _mainstartx + _width - _subImg.width;
                        _fctImage.relativex = _mainstartx + _width - _fctImage.width;
                        if (_subImg.width >= _fctImage.width)
                        {
                            _subImg.relativey = _mainstarty;
                            _fctImage.relativey = _mainstarty + _subImg.length;
                        }
                        else
                        {
                            _fctImage.relativey = 0;
                            _subImg.relativey = _mainstarty + _fctImage.length;
                        }
                    }
                    else
                    { canbefitted = false; }
                }
                else
                {
                    //Fit sub img & fact side by side
                    if ((_width - _ipreamble >= _fctImage.width + _subImg.width))
                    {
                        int _imgmaxlen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _fctImage });
                        //int _imgminlen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _fctImage });
                        if (_remlength > _imgmaxlen + _citImage.length)
                        {
                            _fctImage.topimageinsidearticle = true;
                            canbefitted = true;
                        }
                    }
                    if ((_width - _ipreamble >= _citImage.width + _subImg.width))
                    {
                        int _imgmaxlen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _citImage });
                        //int _imgminlen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _fctImage });
                        if (_remlength > _imgmaxlen + _fctImage.length)
                        {
                            _citImage.topimageinsidearticle = true;
                            canbefitted = true;
                        }
                    }
                    if ((_width > _citImage.width + _fctImage.width))
                    {
                        int _imgmaxlen = GetMaxlengthOfSubImages(new List<Image>() { _fctImage, _citImage });
                        //int _imgminlen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _fctImage });
                        if (_remlength > _imgmaxlen + _subImg.length)
                            canbefitted = true;
                    }
                }

            }
            else if (_citImage != null)
            {
                if ((_width - _ipreamble >= _citImage.width + _subImg.width))
                {
                    if (_remlength >= _citImage.length && _remlength > _subImg.length)
                    {
                        _citImage.topimageinsidearticle = true;
                        canbefitted = true;
                    }
                }
                else if (_remlength > _citImage.length + _subImg.length)
                    canbefitted = true;
            }
            else if (_remlength > _subImg.length)
                canbefitted = true;
        }
        else
        {
            if (_fctImage != null)
            {
                if (_citImage is null)
                {
                    if (_remlength > _fctImage.length)
                        canbefitted = true;
                }
                else
                {
                    int _imglen = GetMaxlengthOfSubImages(new List<Image>() { _citImage, _fctImage });
                    if ((_width - _ipreamble >= _citImage.width + _fctImage.width))
                    {
                        if (_remlength > _citImage.length && _remlength > _fctImage.length)
                        {
                            _citImage.topimageinsidearticle = true;
                            _fctImage.topimageinsidearticle = true;
                            canbefitted = true;
                        }
                    }
                    else if (_remlength > _citImage.length + _fctImage.length)
                        canbefitted = true;
                }

            }
            else if (_citImage != null)
            {
                if ((_width - _ipreamble > _citImage.width) && (_remlength > _citImage.length))
                    canbefitted = true;
            }

        }
        return canbefitted;
    }
    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, List<Image> _fctlist, Image _tcitImage)
    {
        List<Box> _sizes = new List<Box>();

        _sizes = FindAllArticleSizes(_box, _tmainImg, _tsubImg, _fctlist, _tcitImage, null, 0);

        //Try to find the fit by adding whitepaces
        int _totalsizes = _box.avalableLengths().Count();
        int _sizesinlayouts = _sizes.Count();
        int _startwhilelines = ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        while (ModelSettings.clsWhiteSpaceSettings.addwhitespacelines && _sizesinlayouts < _totalsizes
            && _startwhilelines <= ModelSettings.clsWhiteSpaceSettings.maxwhitespacelines)
        {
            foreach (var _vsize in _box.avalableLengths())
            {
                if (_sizes.Where(x => x.width == _vsize).Count() == 0)
                {
                    Box _newbox = Helper.CustomCloneBox(_box);
                    _newbox.origArea += _startwhilelines;
                    _newbox.whitespace = _startwhilelines;
                    List<Box> _whitespaceboxes = FindAllArticleSizes(_newbox, _tmainImg, _tsubImg, _fctlist, _tcitImage, null, _vsize);

                    if (_whitespaceboxes != null && _whitespaceboxes.Count > 0)
                        _sizes.AddRange(_whitespaceboxes);
                }
            }
            _sizesinlayouts = _sizes.Count();
            _startwhilelines += ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        }
        return _sizes;
    }

    private List<Box> GenerateSizeForImageWidthSameAsArticleWidth(Box _box, Image _mainImg, List<Image> _subImages, List<Image> _fctlist, Image _citImage, List<Image> mugshots, int _width)
    {
        Image _fctImage = null;
        List<Box> _lst = null;
        int _allowedlength = -1;
        FullWidthFactSettings fullWidthFactSettings = (FullWidthFactSettings)ModelSettings.fullwidthfactsettings[_box.priority];
        if (fullWidthFactSettings != null && fullWidthFactSettings.factSizes.Contains(_width))
            _allowedlength = fullWidthFactSettings.minHeight;

        if (_subImages.Exists(x => x.width > _width))
            return null;

        if (_fctlist != null && (_fctlist.Exists(x => x.width == _width) && (!ModelSettings.enablefactsatbottom || _allowedlength == -1)))
            return null;
        if (_subImages.Exists(x => x.width == _width) && (!ModelSettings.samesizesubimageallowed || _box.priority == 5))
            return null;

        if (ModelSettings.enablefactsatbottom)
        {
            if (_fctlist != null && _fctlist.Exists(x => x.width == _width && x.length <= _allowedlength))
                return null;

        }

        List<Image> _samewidthSubs = new List<Image>();
        if (_fctlist != null && _fctlist.Count() > 0)
        {
            _fctImage = Helper.CustomCloneImage(_fctlist[0]);
        }

        if (_subImages.Count(x => x.width == _width) > 0)
        {
            _samewidthSubs.AddRange(_subImages.FindAll(x => x.width == _width));
            _subImages.RemoveAll(x => x.width == _width);
        }
        if (_fctImage != null && _fctImage.width == _width)
        {
            if (_subImages.Count == 0)
                _lst = FindAllArticleSizes(_box, _mainImg, null, _citImage, mugshots, _width);
            else if (_subImages.Count == 1)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], null, _citImage, mugshots, _width);
            else if (_subImages.Count == 2)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], _subImages[1], null, _citImage, mugshots, _width);
            else if (_subImages.Count == 3)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], _subImages[1], _subImages[2], null, _citImage, mugshots, _width);

            if (_lst != null && _lst.Count > 0)
            {
                if (_samewidthSubs == null || _samewidthSubs.Count == 0)
                { //Removing empty line below full width facts
                    _fctImage.length -= ModelSettings.extrafactline;
                    _fctImage.captionlength -= ModelSettings.extrafactline;
                }
                _lst[0].length += _fctImage.length + _samewidthSubs.Sum(x => x.length) + ModelSettings.extraLineForTextWrapping;
                _lst[0].usedImageList.Add(_fctImage);
                _lst[0].usedImageList.AddRange(_samewidthSubs);

                //Setting Relative x and y for fact and SubImage
                int _y = 0;
                if (_samewidthSubs.Count > 0)
                {
                    foreach (Image img in _samewidthSubs)
                    {
                        _y += img.length;
                        img.relativex = 0;
                        img.relativey = -_y;
                    }
                }
                _fctImage.relativex = 0;
                _fctImage.relativey = -(_y + _fctImage.length);
            }

        }
        else if (_samewidthSubs.Count > 0)
        {
            if (_subImages.Count == 0)
                _lst = FindAllArticleSizes(_box, _mainImg, _fctlist, _citImage, mugshots, _width);
            else if (_subImages.Count == 1)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], _fctlist, _citImage, mugshots, _width);
            else if (_subImages.Count == 2)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], _subImages[1], _fctlist, _citImage, mugshots, _width);
            else if (_subImages.Count == 3)
                _lst = FindAllArticleSizes(_box, _mainImg, _subImages[0], _subImages[1], _subImages[2], _fctlist, _citImage, mugshots, _width);

            if (_lst != null && _lst.Count > 0)
            {
                _lst[0].length += _samewidthSubs.Sum(x => x.length) + ModelSettings.extraLineForTextWrapping;
                _lst[0].usedImageList.AddRange(_samewidthSubs);

                //Setting Relative x and y for fact and SubImage
                int _y = 0;
                foreach (Image img in _samewidthSubs)
                {
                    _y += img.length;
                    img.relativex = 0;
                    img.relativey = -_y;
                }
            }
        }

        return _lst;
    }
    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, List<Image> _fctlist, Image _tcitImage, List<Image> mugshots, int _boxwidth)
    {
        List<Box> _sizes = new List<Box>();

        double _area;
        Image _fctImage = null, _mainImg, _subImg, _citImage = null;

        List<Image> images = new List<Image> { _tmainImg, _tsubImg };
        var allMugshots = images.Where(img => img.imagetype == "mugshot").ToList();
        var remainingImages = images.Where(img => img.imagetype == "Image").ToList();
        if (allMugshots.Any())
        {
            var typeImageCount = remainingImages.Count;
            if (typeImageCount == 0)
            {
                return FindAllArticleSizesNoMain(_box, _fctlist, _tcitImage, allMugshots);
            }
            else
            {
                return FindAllArticleSizes(_box, remainingImages[0], _fctlist, _tcitImage, allMugshots, _boxwidth);
            }
        }
        if (mugshots != null)
            mugshots = mugshots.Take(ModelSettings.mugshotSetting.maxMugshotAllowed).ToList();

        var mugshotCount = mugshots != null ? mugshots.Count : 0;
        var mugshotsArea = mugshots != null ? mugshots.Sum(x => x.imageMetadata.doubleSize.Area) : 0;
        int _ipreamble = 1 + mugshotCount;

        foreach (int _width in _box.avalableLengths())
        {
            if (_boxwidth > 0 && _width != _boxwidth)
                continue;
            double _iarea1 = 0, _iarea2 = 0, _iarea3 = 0, _iarea4 = 0, _newboxarea = 0;
            double _whitespaceleft = 0;
            _mainImg = Helper.CustomCloneImage(_tmainImg);
            _subImg = Helper.CustomCloneImage(_tsubImg);
            if (_fctlist != null && _fctlist.Count() > 0)
            {
                _fctImage = Helper.CustomCloneImage(_fctlist[0]);
            }
            if (_tcitImage != null)
                _citImage = Helper.CustomCloneImage(_tcitImage);

            if ((_fctImage != null && _fctImage.width == _width) || _subImg.width == _width)
            {
                List<Box> _lst = GenerateSizeForImageWidthSameAsArticleWidth(_box, _mainImg, new List<Image>() { _subImg }, _fctlist, _citImage, mugshots, _width);
                if (_lst != null)
                    _sizes.AddRange(_lst);
                continue;
            }

            _area = _box.origArea;
            _iarea1 = _mainImg.length * _mainImg.width;
            _iarea4 = _subImg.length * _subImg.width;

            if (_fctImage != null)
                _iarea2 = _fctImage.length * _fctImage.width;
            if (_citImage != null)
                _iarea3 = _citImage.length * _citImage.width;
            //_newboxarea = _area + _iarea1 + _iarea2 + _iarea3 + _iarea4;

            List<Image> _subimages = new List<Image>();
            _subimages.Add(_subImg);
            _subimages.Add(_fctImage);
            _subimages.Add(_citImage);

            int _maxsublength = GetMaxlengthOfSubImages(_subimages);
            _newboxarea = _area + _iarea1 + _iarea2 + _iarea3 + _iarea4 + mugshotsArea;
            //Width of the final article cannot be less than the image width
            if (_width < _mainImg.width)
                continue;
            if (_width < _subImg.width)
                continue;
            if (_subImg.width > _mainImg.width)
                continue;
            if (_fctImage != null && (_width < _fctImage.width || !Helper.isValidFact(_fctImage)))
                continue;
            if (_citImage != null && _width < _citImage.width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);
            int _remaininglength = _length;

            if (_length < _box.preamble + _box.byline)
                continue;

            _whitespaceleft = _length * _width - _newboxarea;
            if (_mainImg.width == _width && _subImg.width < _mainImg.width)
            {
                _mainImg.aboveHeadline = true;
                _subImg.topimageinsidearticle = true;
                _remaininglength = _length - _mainImg.length;
                if (!CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, _subimages))
                    continue;

            }
            else if (_mainImg.width == _width && _subImg.width == _width)
            {
                _mainImg.aboveHeadline = true;
                if (!CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, _subimages))
                    continue;
            }
            else
            {
                bool cantproceed = true;
                if (_citImage != null)
                {
                    if (_subImg.width == _citImage.width && _mainImg.width + _subImg.width == _width)
                    {
                        if (_subImg.length + _citImage.length <= _mainImg.length && _subImg.length + _citImage.length > _mainImg.length - _mainImg.captionlength)
                        {
                            if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { null, _fctImage, null }))
                            {
                                cantproceed = false;
                                _mainImg.aboveHeadline = true;
                                _subImg.aboveHeadline = true;
                                _citImage.aboveHeadline = true;
                                _subImg.relativex = _mainImg.width;
                                _subImg.relativey = 0;
                                _citImage.relativex = _mainImg.width;
                                _citImage.relativey = _subImg.length;
                                //_remaininglength = _length - _mainImg.length;
                            }
                        }
                    }
                }

                if (_fctImage != null && cantproceed)
                {
                    if (_mainImg.width + _fctImage.width == _width)
                    {
                        if (_fctImage.length == _mainImg.length)
                        //if (_fctImage.length <= _mainImg.length && _fctImage.length > _mainImg.length - _mainImg.captionlength)
                        {
                            if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { _subImg, null, _citImage }))
                            {
                                cantproceed = false;
                                _mainImg.aboveHeadline = true;
                                _fctImage.aboveHeadline = true;
                                _fctImage.relativex = _mainImg.width;
                                _fctImage.relativey = 0;
                                _subImg.topimageinsidearticle = true;
                            }
                        }
                    }

                    if (cantproceed)
                    {
                        if (_subImg.width == _fctImage.width && _mainImg.width + _subImg.width == _width)
                        {
                            if (_subImg.length + _fctImage.length == _mainImg.length)// && _subImg.length + _fctImage.length > _mainImg.length - _mainImg.captionlength)
                            {
                                if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { null, null, _citImage }))
                                {
                                    cantproceed = false;
                                    _mainImg.aboveHeadline = true;
                                    _subImg.aboveHeadline = true;
                                    _fctImage.aboveHeadline = true;
                                    _subImg.relativex = _mainImg.width;
                                    _subImg.relativey = 0;
                                    _fctImage.relativex = _mainImg.width;
                                    _fctImage.relativey = _subImg.length;
                                }
                            }
                        }
                    }
                }
                //Main mage needs to be fit inside the box then Main and sub images should be placed side by side
                if (cantproceed)
                {
                    //POK If images has to be fit inside then max 3 will be be allowed
                    if (_citImage != null && _fctImage != null)
                    {
                        _length = (int)Math.Ceiling((_newboxarea - (_citImage.length * _citImage.width)) / _width);
                        _citImage = null;
                    }

                    if (CheckImageInvalidLengths(new List<Image>() { _mainImg, _subImg, _fctImage, _citImage }, _length, (int)_whitespaceleft))
                        continue;

                    if (GetMaxlengthOfSubImages(new List<Image>() { _mainImg, _subImg, _fctImage, _citImage }) > _length)
                        continue;

                    if ((_width == _mainImg.width + _ipreamble) && _ipreamble > 0)
                        continue;
                    if ((_mainImg.width < _width - _ipreamble))
                    {
                        //Two images needs to be placed side by side
                        if (_mainImg.width + _subImg.width > _width - _ipreamble)
                            continue;
                        _mainImg.topimageinsidearticle = true;
                        _subImg.topimageinsidearticle = true;
                        _mainImg.relativex = _width - _mainImg.width;
                        if (_subImg.length == _length)
                            _subImg.relativex = _width - _mainImg.width - _subImg.width;
                        else
                            _subImg.relativex = _ipreamble;// + _width-_ipreamble-_mainImg.width-_subImg.width;
                        int _imglen = GetMaxlengthOfSubImages(new List<Image>() { _mainImg, _subImg });
                        if (_fctImage == null && _citImage == null)
                            cantproceed = false;
                        if (_fctImage != null)// && _fctImage.width<= _subImg.width)
                        {
                            if (_fctImage.width <= _subImg.width && _fctImage.length + _subImg.length <= _length - ModelSettings.minimumlinesunderImage)
                            {
                                _fctImage.relativex = _subImg.relativex + (_subImg.width - _fctImage.width) / 2;
                                _fctImage.relativey = _subImg.length;
                                cantproceed = false;
                                _fctImage.parentimageid = _subImg.id;
                            }
                            //fact needs to be placed below the Main image
                            if (_fctImage.width <= _mainImg.width)
                            {
                                if (_fctImage.length + _mainImg.length <= _length - ModelSettings.minimumlinesunderImage || _fctImage.length + _mainImg.length == _length)
                                {
                                    cantproceed = false;
                                    _fctImage.parentimageid = _mainImg.id;
                                    if (_fctImage.length + _mainImg.length == _length)
                                        _fctImage.relativex = _width - _fctImage.width;
                                    else
                                        _fctImage.relativex = _mainImg.relativex + (_mainImg.width - _fctImage.width) / 2;
                                    _fctImage.relativey = _mainImg.length;
                                }
                            }
                        }
                        //Citation should be placed only below subimage
                        if (_citImage != null && _citImage.width <= _subImg.width)
                        {
                            if (_citImage.length + _subImg.length <= _length - ModelSettings.minimumlinesunderImage)
                            {
                                cantproceed = false;
                                _citImage.parentimageid = _subImg.id;
                                _citImage.relativex = _subImg.relativex + (_subImg.width - _citImage.width) / 2;
                                _citImage.relativey = _subImg.length;
                            }

                        }
                    }
                }

                if (cantproceed)
                    continue;

                //Length of the remianing box > preamble
                //EPSLN-42
                if (_mainImg.aboveHeadline && (_length - _mainImg.length < _box.preamble + _box.byline))
                    continue;

            }

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.usedImageList = new List<Image>();
            _newbox.usedImageList.Add(_mainImg);
            _newbox.usedImageList.Add(_subImg);
            if (_fctImage != null)
                _newbox.usedImageList.Add(_fctImage);
            if (_citImage != null)
                _newbox.usedImageList.Add(_citImage);
            if (mugshots != null)
            {
                var mshotList = Helper.CustomCloneListImage(mugshots);
                _newbox.usedImageList.AddRange(mshotList);
            }
            if (Helper.isValidBox(_newbox, _ipreamble))
            {
                _sizes.Add(_newbox);
            }
        }

        return _sizes;
    }

    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, Image _tsubImg2, List<Image> _fctlist, Image _tcitImage, List<Image> mugshots, int _boxwidth)
    {
        List<Image> images = new List<Image> { _tmainImg, _tsubImg, _tsubImg2 };

        var allMugshots = images.Where(img => img.imagetype == "mugshot").ToList();
        var remainingImages = images.Where(img => img.imagetype == "Image").ToList();
        if (allMugshots.Any())
        {
            var typeImageCount = remainingImages.Count;
            if (typeImageCount == 0)
            {
                return FindAllArticleSizesNoMain(_box, _fctlist, _tcitImage, allMugshots);
            }
            else if (typeImageCount == 1)
            {
                return FindAllArticleSizes(_box, remainingImages[0], _fctlist, _tcitImage, allMugshots, _boxwidth);
            }
            else
            {
                return FindAllArticleSizes(_box, remainingImages[0], remainingImages[1], _fctlist, _tcitImage, allMugshots, _boxwidth);
            }
        }

        List<Box> _sizes = new List<Box>();
        if (mugshots != null)
            mugshots = mugshots.Take(ModelSettings.mugshotSetting.maxMugshotAllowed).ToList();


        var mugshotCount = mugshots != null ? mugshots.Count : 0;
        var mArea = mugshots != null ? mugshots.Sum(x => x.imageMetadata.doubleSize.Area) : 0;

        double _area;

        Image _fctImage = null, _mainImg, _subImg, _subImg2, _citImage = null;
        int _ipreamble = 1 + mugshotCount;

        foreach (int _width in _box.avalableLengths())
        {
            if (_boxwidth > 0 && _width != _boxwidth)
                continue;

            _mainImg = Helper.CustomCloneImage(_tmainImg);
            _subImg = Helper.CustomCloneImage(_tsubImg);
            _subImg2 = Helper.CustomCloneImage(_tsubImg2);

            if (_fctlist != null && _fctlist.Count() > 0)
            {
                _fctImage = Helper.CustomCloneImage(_fctlist[0]);
                //_citImage = null; //POK: If the fact is not null then ignore the citation
            }
            else if (_tcitImage != null)
                _citImage = Helper.CustomCloneImage(_tcitImage);

            if ((_fctImage != null && _fctImage.width == _width) || _subImg.width == _width || _subImg2.width == _width)
            {
                List<Box> _lst = GenerateSizeForImageWidthSameAsArticleWidth(_box, _mainImg, new List<Image>() { _subImg, _subImg2 }, _fctlist, _citImage, mugshots, _width);
                if (_lst != null)
                    _sizes.AddRange(_lst);
                continue;
            }
            double _iarea1 = 0, _iarea2 = 0, _iarea3 = 0, _iarea4 = 0, _iarea5 = 0, _newboxarea;

            _area = _box.origArea;
            _iarea1 = _mainImg.length * _mainImg.width;
            _iarea4 = _subImg.length * _subImg.width;
            _iarea5 = _subImg2.length * _subImg2.width;

            if (_fctImage != null)
                _iarea2 = _fctImage.length * _fctImage.width;
            if (_citImage != null)
                _iarea3 = _citImage.length * _citImage.width;
            _newboxarea = _area + _iarea1 + _iarea2 + _iarea3 + _iarea4 + _iarea5 + mArea;

            List<Image> _subimages = new List<Image>();
            _subimages.Add(_fctImage);
            _subimages.Add(_citImage);
            _subimages.Add(_subImg);
            _subimages.Add(_subImg2);
            int _maxsublength = GetMaxlengthOfSubImages(_subimages);

            //Width of the final article cannot be less than the image width
            if (_width < _mainImg.width)
                continue;
            if (_width < _subImg.width || _width < _subImg2.width)
                continue;
            if (_fctImage != null && (_width < _fctImage.width || !Helper.isValidFact(_fctImage)))
                continue;
            if (_citImage != null && _width < _citImage.width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);

            if (_length < _box.preamble + _box.byline)
                continue;

            bool cantproceed = true;
            if (_mainImg.width == _width)
            {
                _mainImg.aboveHeadline = true;
                //POK: If the width of subimages == width, images should be fit above the headline and length should be same
                if (_subImg.width + _subImg2.width == _width)
                {
                    if (_subImg.length != _subImg2.length)
                    {
                        continue;
                    }
                    else
                    {
                        _subImg.aboveHeadline = true;
                        _subImg2.aboveHeadline = true;
                        if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length - _subImg.length, _ipreamble, new List<Image>() { null, _fctImage, _citImage }))
                            cantproceed = false;
                    }
                }
                //for subimages having width equal to the width of article
                else if (_box.priority < 5 && (_subImg.width == _width || _subImg2.width == _width))
                {
                    int _remaininglength = _length - _mainImg.length;
                    if (_citImage != null && _fctImage != null)
                    {
                        _citImage = null;
                        _length = (int)Math.Ceiling((_newboxarea - (_citImage.length * _citImage.width)) / _width);
                        _remaininglength = _length - _mainImg.length;
                    }

                    Image _temp = null;
                    if (_fctImage != null)
                        _temp = _fctImage;
                    else
                        _temp = _citImage;
                    if (_temp == null)
                    {
                        if (_subImg2.width == _subImg.width && _remaininglength >= _subImg.length + _subImg2.length + ((int)Math.Ceiling(_box.origArea / _width)))
                        {
                            _subImg.relativex = 0;
                            _subImg.relativey = -_subImg.length - _subImg2.length;
                            _subImg2.relativex = 0;
                            _subImg2.relativey = -_subImg2.length;
                            cantproceed = false;
                        }
                        else if (_subImg.width == _width && _remaininglength >= _subImg.length + ((int)Math.Ceiling(_box.origArea / _width)))
                        {
                            _subImg2.topimageinsidearticle = true;
                            _subImg.relativex = 0;
                            _subImg.relativey = -_subImg.length;
                            cantproceed = false;
                        }
                        else if (_subImg2.width == _width && _remaininglength >= _subImg2.length + ((int)Math.Ceiling(_box.origArea / _width)))
                        {
                            _subImg.topimageinsidearticle = true;
                            _subImg2.relativex = 0;
                            _subImg2.relativey = -_subImg2.length;
                            cantproceed = false;
                        }
                    }
                    else
                    {
                        int textlen = (int)Math.Ceiling(_box.origArea / _width);
                        if (_subImg2.width == _subImg.width && _temp.width == _width && _remaininglength >= _subImg.length + _subImg2.length + _temp.length + textlen)
                        {
                            _temp.relativex = 0;
                            _temp.relativey = -_temp.length - _subImg.length - _subImg2.length;
                            _subImg.relativex = 0;
                            _subImg.relativey = -_subImg.length - _subImg2.length;
                            _subImg2.relativex = 0;
                            _subImg2.relativey = -_subImg2.length;
                            cantproceed = false;
                        }
                        else if (_subImg2.width == _subImg.width && _remaininglength - _subImg.length - _subImg2.length >= textlen && _temp.length <= textlen)
                        {
                            _temp.topimageinsidearticle = true;
                            _temp.relativex = _ipreamble;
                            _subImg.relativex = 0;
                            _subImg.relativey = -_subImg.length - _subImg2.length;
                            _subImg2.relativex = 0;
                            _subImg2.relativey = -_subImg2.length;
                            cantproceed = false;
                        }
                        else if (_subImg.width == _width || _subImg2.width == _width)
                        {
                            Image fullWidthArticle = _subImg.width == _width ? _subImg : _subImg2;
                            Image smallerArticle = fullWidthArticle == _subImg ? _subImg2 : _subImg;
                            if (_remaininglength - fullWidthArticle.length >= ((int)Math.Ceiling(_box.origArea / _width)))
                            {
                                fullWidthArticle.relativex = 0;
                                fullWidthArticle.relativey = -fullWidthArticle.length;
                                _remaininglength = _remaininglength - fullWidthArticle.length;
                                if (_temp.width <= smallerArticle.width)
                                {
                                    //Sub image +Fact should be placed in the center
                                    if (_temp.length + smallerArticle.length <= _remaininglength - ModelSettings.minimumlinesunderImage)
                                    {
                                        cantproceed = false;
                                        _temp.parentimageid = smallerArticle.id;
                                        smallerArticle.relativex = _ipreamble;
                                        _temp.relativex = _ipreamble + (smallerArticle.width - _temp.width) / 2;
                                        _temp.relativey = smallerArticle.length;
                                    }
                                    //Sub image +Fact should be placed to the right
                                    else if (_temp.length + smallerArticle.length == _remaininglength)
                                    {
                                        cantproceed = false;
                                        _temp.parentimageid = smallerArticle.id;
                                        smallerArticle.relativex = _width - smallerArticle.width;
                                        _temp.relativex = _width - _temp.width;
                                        _temp.relativey = smallerArticle.length;
                                    }
                                }
                            }
                        }
                    }
                }
                //POK: subimages should be tried to fit inside
                else if (_subImg.width + _subImg2.width > _width - _ipreamble)
                    continue;
                else
                {
                    int _remaininglenth = _length - _mainImg.length;
                    if (CheckImageInvalidLengths(new List<Image>() { _subImg, _subImg2, _fctImage, _citImage }, _remaininglenth))
                        continue;
                    //POK: Max items allowed inside the article will be 3.
                    _subImg.topimageinsidearticle = true;
                    _subImg2.topimageinsidearticle = true;
                    //POK: both the images will be fit side by side
                    int _imglen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _subImg2 });
                    //If both _fctImage and citation is not null then make citation null

                    if (_citImage == null && _fctImage == null)
                    {
                        if (_imglen <= _remaininglenth - ModelSettings.minimumlinesunderImage ||
                                _imglen == _remaininglenth)
                            cantproceed = false;
                    }
                    if (_citImage != null && _fctImage != null)
                    {
                        _citImage = null;
                        _length = (int)Math.Ceiling((_newboxarea - (_citImage.length * _citImage.width)) / _width);
                    }

                    Image _temp = null;
                    if (_fctImage != null)
                        _temp = _fctImage;
                    else
                        _temp = _citImage;

                    if (_temp != null)
                    //&& _fctImage.width <= newsubImg.width)
                    {
                        if (_temp.width <= _subImg.width)
                        {
                            //Sub image +Fact should be placed in the center
                            if (_temp.length + _subImg.length <= _remaininglenth - ModelSettings.minimumlinesunderImage)
                            {
                                cantproceed = false;
                                _temp.parentimageid = _subImg.id;
                                _subImg.relativex = _ipreamble;
                                _temp.relativex = _ipreamble + (_subImg.width - _temp.width) / 2;
                                _temp.relativey = _subImg.length;
                                _subImg2.relativex = _width - _subImg2.width;
                            }
                            //Sub image +Fact should be placed to the right
                            else if (_temp.length + _subImg.length == _remaininglenth)
                            {
                                cantproceed = false;
                                _temp.parentimageid = _subImg.id;
                                _subImg.relativex = _width - _subImg.width;
                                _temp.relativex = _width - _temp.width;
                                _temp.relativey = _subImg.length;
                                _subImg2.relativex = _ipreamble;
                            }
                        }
                        if (_temp.width <= _subImg2.width)
                        {
                            //Sub image +Fact should be placed in the center
                            if (_temp.length + _subImg2.length <= _remaininglenth - ModelSettings.minimumlinesunderImage)
                            {
                                cantproceed = false;
                                _temp.parentimageid = _subImg2.id;
                                _subImg2.relativex = _ipreamble;
                                _temp.relativex = _ipreamble + (_subImg2.width - _temp.width) / 2;
                                _temp.relativey = _subImg2.length;
                                _subImg.relativex = _width - _subImg.width;
                            }
                            //Sub image +Fact should be placed to the right
                            else if (_temp.length + _subImg2.length == _remaininglenth)
                            {
                                cantproceed = false;
                                _temp.parentimageid = _subImg2.id;
                                _subImg2.relativex = _width - _subImg2.width;
                                _temp.relativex = _width - _temp.width;
                                _temp.relativey = _subImg2.length;
                                _subImg.relativex = _ipreamble;
                            }
                        }
                    }
                }

            }
            else //Main image width < Article width: We need to fit Main image and side images/citation above the headline
            {
                if (_subImg.width == _subImg2.width && _mainImg.width + _subImg.width == _width)
                {
                    //if (_subImg.length + _subImg2.length == _mainImg.length || _subImg.length + _subImg2.length == _mainImg.length - _mainImg.captionlength)
                    if (_subImg.length + _subImg2.length == _mainImg.length)// && _subImg.length + _subImg2.length >= _mainImg.length - _mainImg.captionlength)
                    {
                        if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { null, _fctImage, _citImage }))
                        {
                            cantproceed = false;
                            _mainImg.aboveHeadline = true;
                            _subImg.aboveHeadline = true;
                            _subImg2.aboveHeadline = true;

                        }
                    }
                }

                //If citation exist, we will have more than 3 images.
                //we need to fit them citation at the top
                if (_citImage != null && cantproceed == true)
                {
                    if (_mainImg.width + _citImage.width != _width)
                        continue;

                    if (_citImage.width != _subImg.width && _citImage.width != _subImg2.width)
                        continue;

                    if (_citImage.width == _subImg.width)
                        //if (_citImage.length + _subImg.length <= _mainImg.length && _citImage.length + _subImg.length > _mainImg.length - _mainImg.captionlength)
                        if (_citImage.length + _subImg.length == _mainImg.length)
                        {
                            if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { _subImg2, _fctImage, null }))
                            {
                                _mainImg.aboveHeadline = true;
                                _subImg.aboveHeadline = true;
                                _citImage.aboveHeadline = true;
                                cantproceed = false;

                            }
                        }

                    if (_citImage.width == _subImg2.width)
                        //if (_citImage.length + _subImg2.length <= _mainImg.length && _citImage.length + _subImg2.length > _mainImg.length - _mainImg.captionlength)
                        if (_citImage.length + _subImg2.length == _mainImg.length)
                        {
                            if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { _subImg, _fctImage, null }))
                            {
                                _mainImg.aboveHeadline = true;
                                _subImg2.aboveHeadline = true;
                                _citImage.aboveHeadline = true;
                                cantproceed = false;

                            }
                        }
                }

                //If fact exist, we will have more than 3 images.
                //we need to fit them fact at the top
                if (_fctImage != null && cantproceed == true)
                {
                    if (_mainImg.width + _fctImage.width != _width)
                        continue;

                    //if (_mainImg.length >= _fctImage.length && _mainImg.length - _mainImg.captionlength < _fctImage.length)
                    if (_mainImg.length == _fctImage.length)
                    {
                        _mainImg.aboveHeadline = true;
                        _fctImage.aboveHeadline = true;
                        int _maxsubimageLen = GetMaxlengthOfSubImages(new List<Image>() { _subImg, _subImg2 });
                        if (_subImg.width + _subImg2.width <= _width - _ipreamble &&
                            (_maxsubimageLen == _length - _mainImg.length || _maxsubimageLen <= _length - _mainImg.length - ModelSettings.minimumlinesunderImage))
                        {
                            cantproceed = false;
                        }

                    }
                    else
                    {
                        if (_fctImage.width != _subImg.width && _fctImage.width != _subImg2.width)
                            continue;

                        if (_fctImage.width == _subImg.width)
                            //if (_fctImage.length + _subImg.length <= _mainImg.length && _fctImage.length + _subImg.length > _mainImg.length - _mainImg.captionlength)
                            if (_fctImage.length + _subImg.length <= _mainImg.length && _fctImage.length + _subImg.length > _mainImg.length - _mainImg.captionlength)
                            {
                                if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { _subImg2, null, _citImage }))
                                {
                                    _mainImg.aboveHeadline = true;
                                    _subImg.aboveHeadline = true;
                                    _fctImage.aboveHeadline = true;
                                    cantproceed = false;

                                }
                            }

                        if (cantproceed && (_fctImage.width == _subImg2.width))
                            //if (_fctImage.length + _subImg2.length == _mainImg.length || _fctImage.length + _subImg2.length == _mainImg.length - _mainImg.captionlength)
                            if (_fctImage.length + _subImg2.length <= _mainImg.length && _fctImage.length + _subImg2.length > _mainImg.length - _mainImg.captionlength)
                            {
                                if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length, _ipreamble, new List<Image>() { _subImg, null, _citImage }))
                                {
                                    _mainImg.aboveHeadline = true;
                                    _subImg2.aboveHeadline = true;
                                    _fctImage.aboveHeadline = true;
                                    cantproceed = false;
                                }
                            }
                    }
                }
            }

            if (cantproceed)
                continue;

            //Length of the remianing box > preamble
            //EPSLN-42
            if (_mainImg.aboveHeadline && (_length - _mainImg.length < _box.preamble + _box.byline))
                continue;

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.usedImageList = new List<Image>();
            _newbox.usedImageList.Add(_mainImg);
            _newbox.usedImageList.Add(_subImg);
            _newbox.usedImageList.Add(_subImg2);
            if (_fctImage != null)
                _newbox.usedImageList.Add(_fctImage);
            if (_citImage != null)
                _newbox.usedImageList.Add(_citImage);

            if (mugshots != null)
                _newbox.usedImageList.AddRange(mugshots);

            if (Helper.isValidBox(_newbox, _ipreamble))
            {
                _sizes.Add(_newbox);
            }

        }
        return _sizes;
    }
    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, Image _tsubImg2, List<Image> _fctlist, Image _tcitImage)
    {

        List<Box> _sizes = new List<Box>();

        _sizes = FindAllArticleSizes(_box, _tmainImg, _tsubImg, _tsubImg2, _fctlist, _tcitImage, null, 0);

        //Try to find the fit by adding whitepaces
        int _totalsizes = _box.avalableLengths().Count();
        int _sizesinlayouts = _sizes.Count();
        int _startwhilelines = ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        while (ModelSettings.clsWhiteSpaceSettings.addwhitespacelines && _sizesinlayouts < _totalsizes
            && _startwhilelines <= ModelSettings.clsWhiteSpaceSettings.maxwhitespacelines)
        {
            foreach (var _vsize in _box.avalableLengths())
            {
                if (_sizes.Where(x => x.width == _vsize).Count() == 0)
                {
                    Box _newbox = Helper.CustomCloneBox(_box);
                    _newbox.origArea += _startwhilelines;
                    _newbox.whitespace = _startwhilelines;

                    List<Box> _whitespaceboxes = FindAllArticleSizes(_newbox, _tmainImg, _tsubImg, _tsubImg2, _fctlist, _tcitImage, null, _vsize);
                    if (_whitespaceboxes != null && _whitespaceboxes.Count > 0)
                        _sizes.AddRange(_whitespaceboxes);
                }
            }
            _sizesinlayouts = _sizes.Count();
            _startwhilelines += ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        }
        return _sizes;
    }

    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, Image _tsubImg2, Image _tsubImg3, List<Image> _fctlist, Image _tcitImage)
    {
        return FindAllArticleSizes(_box, _tmainImg, _tsubImg, _tsubImg2, _tsubImg3, _fctlist, _tcitImage, null, 0);
    }

    public List<Box> FindAllArticleSizes(Box _box, Image _tmainImg, Image _tsubImg, Image _tsubImg2, Image _tsubImg3, List<Image> _fctlist, Image _tcitImage, List<Image> mugshots, int _boxwidth)
    {
        List<Image> allImages = new List<Image> { _tmainImg, _tsubImg, _tsubImg2, _tsubImg3 };
        var allMugshots = allImages.Where(img => img.imagetype == "mugshot").ToList();
        var remainingImages = allImages.Where(img => img.imagetype == "Image").ToList();
        if (allMugshots.Any())
        {
            var typeImageCount = remainingImages.Count;
            if (typeImageCount == 0)
            {
                return FindAllArticleSizesNoMain(_box, _fctlist, _tcitImage, allMugshots);
            }
            else if (typeImageCount == 1)
            {
                return FindAllArticleSizes(_box, remainingImages[0], _fctlist, _tcitImage, allMugshots, _boxwidth);
            }
            else if (typeImageCount == 2)
            {
                return FindAllArticleSizes(_box, remainingImages[0], remainingImages[1], _fctlist, _tcitImage, allMugshots, _boxwidth);

            }
            else
            {
                return FindAllArticleSizes(_box, remainingImages[0], remainingImages[1], remainingImages[2], _fctlist, _tcitImage, allMugshots, _boxwidth);
            }
        }

        List<Box> _sizes = new List<Box>();
        double _area;
        Image _fctImage = null, _mainImg, _subImg, _subImg2, _subImg3, _citImage = null;

        if (mugshots != null)
            mugshots = mugshots.Take(ModelSettings.mugshotSetting.maxMugshotAllowed).ToList();

        var mCount = mugshots != null ? mugshots.Count : 0;
        var mArea = mugshots != null ? mugshots.Sum(x => x.imageMetadata.doubleSize.Area) : 0;
        int _ipreamble = 1 + mCount;
        foreach (int _width in _box.avalableLengths())
        {
            if (_boxwidth > 0 && _width != _boxwidth)
                continue;

            _mainImg = Helper.CustomCloneImage(_tmainImg);
            _subImg = Helper.CustomCloneImage(_tsubImg);
            _subImg2 = Helper.CustomCloneImage(_tsubImg2);
            _subImg3 = Helper.CustomCloneImage(_tsubImg3);

            if (_fctlist != null && _fctlist.Count() > 0)
            {
                _fctImage = Helper.CustomCloneImage(_fctlist[0]);
                //_citImage = null; //POK: If the fact is not null then ignore the citation
            }
            else if (_tcitImage != null)
                _citImage = Helper.CustomCloneImage(_tcitImage);

            if ((_fctImage != null && _fctImage.width == _width) || _subImg.width == _width || _subImg2.width == _width || _subImg3.width == _width)
            {
                List<Box> _lst = GenerateSizeForImageWidthSameAsArticleWidth(_box, _mainImg, new List<Image>() { _subImg, _subImg2, _subImg3 }, _fctlist, _citImage, mugshots, _width);
                if (_lst != null)
                    _sizes.AddRange(_lst);
                continue;
            }

            //POK: THIS NEEDS TO BE REMOVED
            //if (_fctImage != null)
            //    _citImage = null;
            double _iarea1 = 0, _iarea2 = 0, _iarea3 = 0, _iarea4 = 0, _iarea5 = 0, _iarea6 = 0, _newboxarea;

            _area = _box.origArea;
            _iarea1 = _mainImg.length * _mainImg.width;
            _iarea4 = _subImg.length * _subImg.width;
            _iarea5 = _subImg2.length * _subImg2.width;
            _iarea6 = _subImg3.length * _subImg3.width;

            if (_fctImage != null)
                _iarea2 = _fctImage.length * _fctImage.width;
            if (_citImage != null)
                _iarea3 = _citImage.length * _citImage.width;
            _newboxarea = _area + _iarea1 + _iarea2 + _iarea3 + _iarea4 + _iarea5 + _iarea6 + mArea;


            //Width of the final article cannot be less than the image width
            if (_width < _mainImg.width)
                continue;
            if (_width < _subImg.width || _width < _subImg2.width || _width < _subImg3.width)
                continue;
            if (_mainImg.width < _subImg.width || _mainImg.width < _subImg2.width || _mainImg.width < _subImg3.width)
                continue;
            if (_fctImage != null && (_width < _fctImage.width || !Helper.isValidFact(_fctImage)))
                continue;
            if (_citImage != null && _width < _citImage.width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);

            if (_length < _box.preamble + _box.byline)
                continue;

            bool cantproceed = true;
            if (_mainImg.width == _width)
            {
                _mainImg.aboveHeadline = true;
                //POK: If the width of subimages == width, images should be fit above the headline and length should be same
                if (_subImg.width + _subImg2.width + _subImg3.width == _width)
                {
                    if (_subImg.length == _subImg2.length && _subImg.length == _subImg3.length)
                    {
                        if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length - _subImg.length, _ipreamble, new List<Image>() { null, _fctImage, _citImage }))
                        {
                            _subImg.aboveHeadline = true;
                            _subImg2.aboveHeadline = true;
                            _subImg3.aboveHeadline = true;
                            cantproceed = false;
                        }
                    }
                }
                if (cantproceed && _subImg.width + _subImg2.width == _width)
                {
                    if (_subImg.length == _subImg2.length)
                    {
                        List<Image> images = new List<Image>() { _subImg3, _fctImage, _citImage };
                        if (CanImagesBeFittedInsideBox(_box, _width, _length - _mainImg.length - _subImg.length, _ipreamble, images))
                        {
                            _subImg.aboveHeadline = true;
                            _subImg2.aboveHeadline = true;
                            cantproceed = false;
                        }
                    }
                }
            }
            else //Main image width < Article width: We need to fit Main image and side images/citation above the headline
            {
                if (_subImg.width == _subImg2.width && _subImg.width == _subImg3.width && _mainImg.width + _subImg.width == _width)
                {
                    int _totalsublength = _subImg.length + _subImg2.length + _subImg3.length;
                    if (_totalsublength == _mainImg.length)// && _totalsublength > _mainImg.length - _mainImg.captionlength)
                    {
                        cantproceed = false;
                        _mainImg.aboveHeadline = true;
                        _subImg.aboveHeadline = true;
                        _subImg2.aboveHeadline = true;
                        _subImg3.aboveHeadline = true;
                    }
                }
            }

            if (cantproceed)
                continue;
            //Length of the remianing box > preamble //EPSLN-42
            //POK: We need ot handle the case when subimages are placed below the main image
            if (_mainImg.aboveHeadline && (_length - _mainImg.length < _box.preamble + _box.byline))
                continue;

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.usedImageList = new List<Image>();
            _newbox.usedImageList.Add(_mainImg);
            _newbox.usedImageList.Add(_subImg);
            _newbox.usedImageList.Add(_subImg2);
            _newbox.usedImageList.Add(_subImg3);
            if (_fctImage != null)
                _newbox.usedImageList.Add(_fctImage);
            if (_citImage != null)
                _newbox.usedImageList.Add(_citImage);
            if (mugshots != null)
            {
                _newbox.usedImageList.AddRange(mugshots);
            }
            if (Helper.isValidBox(_newbox, _ipreamble))
            {
                _sizes.Add(_newbox);
            }
        }

        return _sizes;
    }

    private bool BuildDoubleTruckPage_FullHeadline(Box _dtarticle, PageInfo _pinfo)
    {
        bool hasfit = false;

        List<ImageScoreList> _lstscores = new List<ImageScoreList>();
        List<Box> _boxlist = FindArticlePermutationsForDoubleTruck(_dtarticle, _pinfo);
        _boxlist = _boxlist.OrderByDescending(x => x.length).ToList();
        int _minlength = (int)_boxlist[_boxlist.Count() - 1].length;


        int _starty = _pinfo.sectionheaderheight;
        string _mainimageid = _dtarticle.imageList[0].id;
        Dictionary<int, List<Image>> _dictImages = new Dictionary<int, List<Image>>();

        for (int _i = 0; _i < _dtarticle.imageList.Count(); _i++)
        {
            //if (_i > _dtarticle.imageList.Count() - 1)
            //    break;
            Image _image = _dtarticle.imageList[_i];
            var _imglist = Image.GetAllPossibleImageSizesDT(_image, _dtarticle, _i == 0 ? 1 : 0);
            _dictImages.Add(_image.priority, _imglist);
        }
        int _maxlength = canvasz - _starty - _dictImages[0].OrderByDescending(x => -1 * x.length).ToList()[0].length;

        for (int _i = _minlength; _i <= _maxlength; _i++)
        {
            int _availableheight = canvasz - _i - _starty;
            //Image _mainimageinstance = null;
            List<Image> _mainimageList = null;
            if (_dictImages[0].Where(x => x.length == _availableheight).ToList().Count() > 0)
                _mainimageList = _dictImages[0].Where(x => x.length == _availableheight).ToList();
            else
                continue;

            foreach (var _mainimageinstance in _mainimageList)
            {
                int _remainingwidth = canvasx * 2 - _mainimageinstance.width;
                var _tscore = FindBestLayoutForDoubleTruck(_dictImages, _mainimageinstance.width, _starty, _remainingwidth, _mainimageinstance.length, _pinfo, _mainimageinstance);
                _lstscores.AddRange(_tscore);
            }
        }

        if (_lstscores == null || _lstscores.Count() == 0)
        {
            hasfit = false;
            Log.Information("Couldn't generate layout for Double Truck");
            return hasfit;
        }

        _lstscores = _lstscores.OrderByDescending(x => x.finalscore - x.totalarea).ThenByDescending(x => x.uniqueid).ToList();
        List<ImageScoreList> _finalScoreList = new List<ImageScoreList>();
        if (_lstscores.Where(x => x.finalscore - x.totalarea == 0).ToList().Count() == 0)
            _finalScoreList.Add(_lstscores[0]);
        else
        {
            _finalScoreList = _lstscores.Where(x => x.finalscore - x.totalarea == 0).OrderByDescending(x => x.uniqueid).ToList();
            BigInteger _tmpid = _finalScoreList[0].uniqueid;
            // _finalScoreList = _finalScoreList.Where(x => x.uniqueid == _tmpid).OrderByDescending (x=>-1*x._mainImage.croppercentage).ToList();
        }

        List<List<Image>> _lstlstfactImages = new List<List<Image>>();
        if (_dtarticle.factList != null && _dtarticle.factList.Count() > 0)
        {
            foreach (var fact in _dtarticle.factList)
            {
                List<Image> _lstFactImages = Image.GetAllPossibleImageSizes(fact, _dtarticle, 0);
                _lstlstfactImages.Add(_lstFactImages);
            }
        }

        List<Image> _lstCitationImages = new List<Image>();
        if (_dtarticle.citationList != null && _dtarticle.citationList.Count() > 0)
        {
            _lstCitationImages = Image.GetAllPossibleImageSizes(_dtarticle.citationList[0], _dtarticle, 0);
        }
        List<ScoreList> _lstfinalScores = new List<ScoreList>();
        //Find the best layout for Score
        foreach (var _item in _finalScoreList)
        {
            Headline _headline = (Headline)headlines[_dtarticle.Id];
            int _hlheight = _headline.GetHeadlineHeight("large", 2 * canvasx);
            int _kickerlength = 0;
            if (kickersmap.Count > 0 && kickersmap[_dtarticle.Id] != null)
            {
                _kickerlength = (int)((Kicker)kickersmap[_dtarticle.Id]).collinemap[2 * canvasx];
            }
            Image _mainimage = _item._mainImage;
            int _availableboxlength = canvasz - _pinfo.sectionheaderheight - _mainimage.length - _hlheight - _kickerlength;
            int _availablearea = _availableboxlength * canvasx * 2;
            int _remianingareaforimages = _availablearea - (int)Math.Ceiling((decimal)_dtarticle.origArea);
            int _maxImagePriority = _item.boxes.OrderByDescending(x => x.priority).ToList()[0].priority;

            List<Image> _lstRemainingImage = _dtarticle.imageList.Where(x => x.priority > _maxImagePriority).OrderByDescending(x => x.priority).ToList();
            List<Image> _lstFirstImage = null;
            List<Image> _lstSecondImage = null;
            if (_lstRemainingImage.Count() > 0)
                _lstFirstImage = _dictImages[_lstRemainingImage[0].priority];
            if (_lstRemainingImage.Count() > 1)
                _lstSecondImage = _dictImages[_lstRemainingImage[1].priority];

            List<ImageScoreList> _lstImageScore = Helper.GetAllImagePermutationsForDoubleTruck(Helper.DeepCloneListListImage(_lstlstfactImages), Helper.DeepCloneListImage(_lstFirstImage),
                Helper.DeepCloneListImage(_lstSecondImage), Helper.DeepCloneListImage(_lstCitationImages), _availableboxlength);
            //find best case
            List<ImageScoreList> _tlist3 = _lstImageScore.Where(x => x.totalarea <= _remianingareaforimages).OrderByDescending(x => x.totalarea - _remianingareaforimages).ToList();
            // List<ImageScoreList> _tlist2 = _lstImageScore.Where(x => x.imagecount == 2 && x.totalarea <= _remianingareaforimages).OrderByDescending(x => x.totalarea).ToList();
            //List<ImageScoreList> _tlist1 = _lstImageScore.Where(x => x.imagecount == 1 && x.totalarea <= _remianingareaforimages).OrderByDescending(x => x.totalarea).ToList();
            ImageScoreList _scorelist = null;
            if (_tlist3 != null && _tlist3.Count() > 0)
                _scorelist = _tlist3[0];
            //else if (_tlist2 != null && _tlist2.Count() > 0)
            //    _scorelist = _tlist2[0];
            //else if (_tlist1 != null && _tlist1.Count() > 0)
            //    _scorelist = _tlist1[0];

            if (_scorelist == null)
                continue;

            Box _tbox = Helper.DeepCloneBox(_dtarticle);
            _tbox.width = 2 * canvasx;
            _tbox.length = canvasz - _pinfo.sectionheaderheight;
            Node n = new Node() { pos_x = 0, pos_z = _pinfo.sectionheaderheight, width = 2 * canvasx, length = canvasz - _pinfo.sectionheaderheight, isOccupied = true };
            _tbox.position = n;
            _tbox.pos_x = 0;
            _tbox.pos_z = _pinfo.sectionheaderheight;
            _tbox.headlinewidth = (int)_tbox.width;
            _tbox.headlinelength = _hlheight;
            _tbox.headlinecaption = "large";
            _tbox.kickerlength = _kickerlength;

            _tbox.usedImageList = new List<Image>();
            _tbox.usedImageList.Add(_item._mainImage);
            foreach (var _img in _item.boxes)
                _tbox.usedImageList.Add(_img);

            foreach (var _img in _scorelist.boxes)
                _tbox.usedImageList.Add(_img);

            Helper.PlaceImagesInsideArticleForDoubleTruck(_tbox, _scorelist.boxes, _pinfo.sectionheaderheight + _mainimage.length + _hlheight + _kickerlength);

            ScoreList _tsc = new ScoreList(new List<Box>() { _tbox }, canvasx * canvasz * 2);
            _tsc.calculatespace();
            _lstfinalScores.Add(_tsc);
        }
        if (_lstfinalScores != null && _lstfinalScores.Count() > 0)
        {
            hasfit = true;
            _lstfinalScores = _lstfinalScores.OrderByDescending(x => x.boxes[0].usedImageList.Count).ThenByDescending(x => x.areafilled).ThenByDescending(x => -1 * x.boxes[0].usedImageList[0].croppercentage).ToList();
            //_lstfinalScores = _lstfinalScores.OrderByDescending(x => x.areafilled).ThenByDescending(x => -1 * x.boxes[0].usedImageList[0].croppercentage).ToList();
            _pinfo.sclist = _lstfinalScores[0];
        }
        else
            hasfit = false;

        return hasfit;
    }

    private bool BuildDoubleTruckPage(Box _dtarticle, PageInfo _pinfo, PageInfo _spinfo)
    {
        bool hasfit = false;
        Dictionary<string, List<Box>> _relatedarticlesPermutation = new Dictionary<string, List<Box>>();
        List<Box> _boxlist = FindArticlePermutationsForDoubleTruck(_dtarticle, _pinfo);
        _boxlist = _boxlist.OrderByDescending(x => -1 * x.length).ToList();
        int _minlength = (int)_boxlist[0].length;
        _boxlist = _boxlist.OrderByDescending(x => -1 * x.length * x.width).ToList();
        int _minboxArea = (int)(_boxlist[0].width * _boxlist[0].length);

        List<Box> _relatedArticles = articles.Where(x => x.parentArticleId == _dtarticle.Id && x.category.ToLower() == _dtarticle.category.ToLower()).OrderByDescending(x => x.priority).ToList();
        foreach (var relArticle in _relatedArticles)
        {
            relArticle.imageList?.RemoveAll(img => img.imagetype == "mugshot");
        }
        //RP: Fix for FLOW-267
        if (!ModelSettings.hasloftarticle)
        {
            if (_relatedArticles.Count > ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles)
            {
                _relatedArticles.RemoveRange(ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles, _relatedArticles.Count - ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles);
            }
        }
        else
        {
            int _totalrelatedarticlecount = _relatedArticles.Count(x => x.articletype != "loft");
            if (_totalrelatedarticlecount > ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles)
            {
                _relatedArticles.Where(x => x.articletype != "loft").ToList().RemoveRange(ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles, _totalrelatedarticlecount - ModelSettings.clsDoubleTruck.clsRelatedArticles.maxarticles);
            }
        }

        foreach (Box _box in _relatedArticles)
        {
            List<Box> _tlist = FindAllArticlePermutations(_box, 0);
            Helper.RemoveDuplicateArticleSizes(_tlist);
            _relatedarticlesPermutation.Add(_box.Id, _tlist);
        }
        //hasfit = BuildDoubleTruckPage_FullHeadline(_dtarticle, _pinfo);
        DoubleTruck _clsDT = new DoubleTruck(_dtarticle, _pinfo, _minlength, headlines, kickersmap, _minboxArea, _relatedarticlesPermutation, _boxlist, _spinfo, _relatedArticles);
        if (ModelSettings.clsDoubleTruck.layouttype.Contains("partialheadline") ||
           ModelSettings.clsDoubleTruck.layouttype.Contains("topfullheadline"))
        {
            hasfit = _clsDT.GenerateDoubleTruckLayout();
        }

        return hasfit;
    }

    private ImageScoreList RunPackerForDoubleTruck(List<List<Image>> result, int _x, int _y, int _width, int _height, PageInfo _pinfo, Image _mimage)
    {
        ImageScoreList score = null;
        List<Image> _packBox = new List<Image>();
        ImagePacker packer = new ImagePacker();
        int _tt;
        foreach (List<Image> _resultitem in result)
        {

            _packBox = new List<Image>();
            int _area = 0;
            BigInteger _newid = 0;
            List<Image> _nresultitem = Helper.DeepCloneListImage(_resultitem.OrderByDescending(x => -1 * x.priority).ToList());
            //1st item caption should be large
            // if (_nresultitem[0].headlinecaption.ToUpper() != "LARGE")
            //     continue;

            foreach (var _imgtem in _nresultitem)
            {
                _imgtem.aboveHeadline = true;
                _packBox.Add(_imgtem);
                _area += _imgtem.length * _imgtem.width;
                _newid = _newid + _imgtem.imageorderId;
            }

            if (_area > _width * _height)
                continue;


            List<Image> _outputlist = packer.StartPacking(_packBox, _x, _y, _width, _height);
            //Check if all the items have been placed
            bool _allitemplaced = true;
            foreach (var _item in _outputlist)
            {
                if (_item.position == null)
                {
                    _allitemplaced = false;
                    break;
                }
            }

            if (!_allitemplaced)
                continue;

            int _newscore = _area;

            if (score == null)
            {
                score = new ImageScoreList(_outputlist, _width * _height);
                score.finalscore = _newscore;
                score.pageid = _pinfo.pageid;
                score.uniqueid = _newid;
                score._mainImage = _mimage;
            }
            else
            {
                if (score.finalscore < _newscore)
                {
                    score.finalscore = _newscore;
                    score.boxes = _outputlist;
                }
            }

        }

        return score;
    }
    private List<ImageScoreList> FindBestLayoutForDoubleTruck(Dictionary<int, List<Image>> _dictImages, int _x, int _y, int _width, int _height, PageInfo _pinfo, Image _mimage)
    {
        List<ImageScoreList> _lstScores = new List<ImageScoreList>();
        ImageScoreList score = null;

        List<List<Image>> result = new List<List<Image>>();

        _mimage.aboveHeadline = true;
        //For 6 image combination
        if (_dictImages.Count() > 6)
        {
            result = Helper.GetPermutationsForSixImages(_dictImages, _width);

            if (result.Count() > 0)
            {
                score = RunPackerForDoubleTruck(result, _x, _y, _width, _height, _pinfo, _mimage);
                if (score != null)
                {
                    _lstScores.Add(score);
                    score.imagealigned = true;
                }
            }
        }
        //For 4 image combination
        if (_dictImages.Count() > 4)
        {
            result = Helper.GetPermutationsForFourImages(_dictImages, _width);

            if (result.Count() > 0)
            {
                score = RunPackerForDoubleTruck(result, _x, _y, _width, _height, _pinfo, _mimage);
                if (score != null)
                {
                    score.imagealigned = true;
                    _lstScores.Add(score);
                }
            }
        }

        //Run the loop for 3->2
        int _imagecounter = 3;
        while (_imagecounter >= 3)
        {
            if (_dictImages.Count() > _imagecounter)
            {
                List<List<Image>> listOfLists = new List<List<Image>>();
                for (int _i = 1; _i <= _imagecounter; _i++)
                {
                    listOfLists.Add(_dictImages[_i]);
                }
                result = Helper.CrossJoin(listOfLists);
                score = RunPackerForDoubleTruck(result, _x, _y, _width, _height, _pinfo, _mimage);
                if (score != null)
                    _lstScores.Add(score);
            }
            else
                break;
            _imagecounter--;
        }

        return _lstScores;
    }

    private List<FinalScores> BuildPage_V6(int _pagenum, List<PageInfo> _lstpages, List<Box> _priorityitems, string _section, List<Box> lowItems, int _maxAItems, int _mandatoryOrder)
    {
        List<FinalScores> _lstFinalScores = new List<FinalScores>();
        _numpermutations = (_mandatoryOrder == 1) ? 1 : _numpermutations;

        int _totPermutations = 0;
        List<Box> _boxes = new List<Box>();
        List<Box> tempList = null;
        List<Thread> pagethreads = new List<Thread>();

        int _totalpagearea = 0;
        foreach (var _tpage in _lstpages)
        {
            _totalpagearea += _tpage._pagearea;
        }
        tempList = new List<Box>();
        List<BoxList> _allEligibleArticles = null;

        if (_mandatoryOrder == 1)
            _allEligibleArticles = GetMaxPriorityItems_MandatoryOrder(_priorityitems, lowItems, _lstpages[0]);

        else
            _allEligibleArticles = FindMaxHighPriorityItems_V1(_lstpages.Count(), _pagenum, _priorityitems, lowItems, _lstpages[0], _section, _totalpagearea);



        List<ScoreList> _fullScoreList = new List<ScoreList>();
        Console.WriteLine(DateTime.Now);

        List<List<Box>> lstLowBoxes = new List<List<Box>>();

        _globalfulllist = new List<ScoreList>();
        Log.Information("Total Permutations for 1st page: {count}", _allEligibleArticles.Count());
        Console.WriteLine("Total Permutations for 1st page: " + _allEligibleArticles.Count());

        foreach (var _tempAllArticles in _allEligibleArticles)
        {
            _totPermutations++;

            var cr = _totPermutations;

            List<Box> _newlist = _tempAllArticles.boxlist.ToList();

            if (_lstpages[0].ads == null || _lstpages[0].ads.Count() == 0)
            {
                BigInteger _allboxesscore = 0;
                GetNextPage_V4_Thread(_pagenum, _lstpages[0], _newlist, _lstpages.Count(), _allboxesscore, cr, _mandatoryOrder);
            }
            else
            {
                GetNextPage_V4_Thread(_pagenum, _lstpages[0], _newlist, _lstpages.Count(), 0, cr, _mandatoryOrder);
            }


        } // End of the High Priority List


        Console.WriteLine(DateTime.Now);

        _fullScoreList.AddRange(_globalfulllist);

        foreach (var _scorelist in _fullScoreList)
        {
            FinalScores _tfinalcore = new FinalScores() { TotalScore = _scorelist.finalscore, articlesprinted = _scorelist.boxes.Count(), totalUniqueId = _scorelist.uniqueid };
            _tfinalcore.articlesprinted = _scorelist.boxes.Where(x => x.priority >= 3).ToList().Count();
            _tfinalcore.optionalarticlesprinted = _scorelist.boxes.Where(x => x.priority < 3).ToList().Count();
            _tfinalcore.pagesprinted = 1;
            _tfinalcore.lstScores.Add(_scorelist);
            _lstFinalScores.Add(_tfinalcore);
        }

        _lstFinalScores = Helper.RemoveDuplicates(_lstFinalScores);

        _lstFinalScores = _lstFinalScores.OrderByDescending(x => x.articlesprinted).ThenByDescending(x => x.optionalarticlesprinted).ThenByDescending(x => x.TotalScore).ToList();
        if (_lstFinalScores.Count() > _numpermutations)
        {
            _lstFinalScores.RemoveRange(_numpermutations, _lstFinalScores.Count() - _numpermutations);
        }

        int _tmppage = _pagenum + 1;

        _totalpagearea = _totalpagearea - _lstpages[0]._pagearea;

        if (_lstFinalScores.Count == 0)
        {
            ScoreList _tscore = new ScoreList(null, canvasx * canvasz);
            _tscore.pageid = _lstpages[0].sname + _lstpages[0].pageid;
            FinalScores _tfinalcore = new FinalScores() { TotalScore = 0, articlesprinted = 0, totalUniqueId = 0 };
            _tfinalcore.lstScores.Add(_tscore);
            _lstFinalScores.Add(_tfinalcore);
        }
        while (_tmppage <= _lstpages.Count() && _lstFinalScores.Count() > 0)
        {
            Log.Information(" Printing page: {page}, permutations: {perms}", _tmppage, _lstFinalScores.Count());
            List<ScoreList> _tmpScoreList;
            List<Box> _thplist = new List<Box>();

            List<FinalScores> _tmpFinalScores = _lstFinalScores.ToList();
            int _permcount = 0;

            lstArticlesAddedOnNextPage.Clear();
            foreach (var _fscore in _tmpFinalScores)
            {
                _permcount++;
                Log.Information(" Page: {page}, permutation #: {permCount}", _tmppage, _permcount);

                List<Box> _tlist = new List<Box>();
                List<Box> _remhighpriorityList = Helper.GetRemainingHighitems(_fscore.lstScores, _section, _priorityitems, _lstpages[_tmppage - 1].articleList, _mandatoryOrder);
                _tlist = _remhighpriorityList.ToList();

                List<Box> _lowpriorityList = Helper.GetRemainingLowitems(_fscore.lstScores, _section, lowItems, _lstpages[_tmppage - 1].articleList, _mandatoryOrder);
                _tlist.AddRange(_lowpriorityList);

                if (_tlist.Count() > 0)
                {
                    _tmpScoreList = null;
                    _tmpScoreList = BuildNextPage_V6(_tmppage, _lstpages[_tmppage - 1], _tlist, _lstpages.Count(), _section, _totalpagearea, _mandatoryOrder);
                    if (_tmpScoreList == null || _tmpScoreList.Count() == 0)
                    {
                        ScoreList _tscore = new ScoreList(null, canvasx * canvasz);
                        _tscore.pageid = _lstpages[_tmppage - 1].sname + _lstpages[_tmppage - 1].pageid;
                        _fscore.lstScores.Add(_tscore);
                    }
                    else
                    {
                        _lstFinalScores.Remove(_fscore);
                        foreach (var _scorelist in _tmpScoreList)
                        {
                            FinalScores _tfinalcore = new FinalScores() { TotalScore = _fscore.TotalScore, articlesprinted = _fscore.articlesprinted, optionalarticlesprinted = _fscore.optionalarticlesprinted, totalUniqueId = _fscore.totalUniqueId, pagesprinted = _fscore.pagesprinted };
                            _tfinalcore.lstScores.AddRange(_fscore.lstScores);
                            _tfinalcore.lstScores.Add(_scorelist);
                            _tfinalcore.TotalScore = _tfinalcore.TotalScore + _scorelist.finalscore;
                            _tfinalcore.articlesprinted = _tfinalcore.articlesprinted + _scorelist.boxes.Where(x => x.priority >= 3).ToList().Count();
                            if (_scorelist.finalscore > 0)
                                _tfinalcore.pagesprinted += 1;
                            _tfinalcore.optionalarticlesprinted += _scorelist.boxes.Where(x => x.priority < 3).ToList().Count();
                            _tfinalcore.totalUniqueId = _tfinalcore.totalUniqueId + _scorelist.uniqueid;
                            _lstFinalScores.Add(_tfinalcore);
                        }
                    }
                }
                else //no articles left
                {
                    ScoreList _tscore = new ScoreList(null, canvasx * canvasz);
                    _tscore.pageid = _lstpages[_tmppage - 1].sname + _lstpages[_tmppage - 1].pageid;
                    _fscore.lstScores.Add(_tscore);
                }
            }

            _lstFinalScores = Helper.RemoveDuplicates(_lstFinalScores);
            _lstFinalScores = _lstFinalScores.OrderByDescending(x => x.articlesprinted).ThenByDescending(x => x.optionalarticlesprinted).ThenByDescending(x => x.TotalScore).ToList();
            if (_lstFinalScores.Count() > _numpermutations)
            {
                _lstFinalScores.RemoveRange(_numpermutations, _lstFinalScores.Count() - _numpermutations);
            }

            _totalpagearea = _totalpagearea - _lstpages[_tmppage - 1]._pagearea;
            _tmppage++;

        }
        return _lstFinalScores;
    }

    private List<ScoreList> BuildNextPage_V6(int _pagenum, PageInfo _pinfo, List<Box> _tList, int _totalpages, string _section, int _totalpagearea, int _mandatoryOrder)
    {

        List<Box> _boxes = new List<Box>();
        List<ScoreList> _fullScoreList = new List<ScoreList>();

        try
        {
            List<Box> _lowlist = _tList.Where(x => x.priority < ModelSettings.highPriorityArticleStart).ToList();
            List<Box> _highlist = _tList.Where(x => x.priority >= ModelSettings.highPriorityArticleStart).ToList();

            List<BoxList> _allEligibleArticles = null;
            if (_mandatoryOrder == 1)
                _allEligibleArticles = GetMaxPriorityItems_MandatoryOrder(_highlist, _lowlist, _pinfo);
            else
                _allEligibleArticles = FindMaxHighPriorityItems_V1(_totalpages, _pagenum, _highlist, _lowlist, _pinfo, _section, _totalpagearea);

            List<List<Box>> listOfLists = new List<List<Box>>();
            _globalfulllist = new List<ScoreList>();
            int _currentrun = 0;
            foreach (var _tempAllArticles in _allEligibleArticles)
            {
                _currentrun++;
                var _local = _currentrun;
                listOfLists = new List<List<Box>>();
                List<Box> _newlist = _tempAllArticles.boxlist.ToList();

                BigInteger _allboxesscore = 0;
                foreach (var _temoBox in _newlist)
                {
                    _allboxesscore += _temoBox.boxorderId;
                }

                if (lstArticlesAddedOnNextPage.Contains(_allboxesscore))
                    continue;
                else
                    lstArticlesAddedOnNextPage.Add(_allboxesscore);

                if (_pinfo.ads == null || _pinfo.ads.Count() == 0)
                {


                    GetNextPage_V4_Thread(_pagenum, _pinfo, _newlist, _totalpages, _allboxesscore, _local, _mandatoryOrder);
                }
                else
                {
                    GetNextPage_V4_Thread(_pagenum, _pinfo, _newlist, _totalpages, 0, _local, _mandatoryOrder);
                }

            }

            _fullScoreList.AddRange(_globalfulllist);
            _fullScoreList = _fullScoreList.OrderByDescending(x => x.uniqueid).ThenByDescending(y => y.finalscore).ToList();

            System.Numerics.BigInteger _prevuniqueid = 0;
            for (int _i = 0; _i < _fullScoreList.Count(); _i++)
            {
                if (_fullScoreList[_i].uniqueid == _prevuniqueid)
                {
                    _fullScoreList.RemoveAt(_i);
                    _i--;
                }
                else
                {
                    _prevuniqueid = _fullScoreList[_i].uniqueid;
                }
            }
            return _fullScoreList;
        }
        catch (Exception ex)
        {
            string s = ex.Message;
            return null;
        }
    }
    //Umesh :=> Flow-148, GetMaxPriorityItems_MandatoryOrder | Get combination of eligible
    private List<BoxList> GetMaxPriorityItems_MandatoryOrder(List<Box> highArticles, List<Box> lowArticles, PageInfo pageInfo)
    {
        BoxList boxes = new BoxList();
        int remainingArea = pageInfo._availablepagearea;
        // Attempt to add the all priority 5/A articles if it fits
        var mandatoryArticles = highArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.priority == 5 && x.articletype.ToLower() == "").ToList();

        //briefs are mandatory
        var briefArticles = highArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.articletype.ToLower() == "briefs").ToList();
        if (briefArticles.Count > 0)
        {
            highArticles.Remove(briefArticles.First());
            mandatoryArticles.Add(briefArticles.First());
        }

        briefArticles.Clear();
        briefArticles = lowArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.articletype.ToLower() == "briefs").ToList();
        if (briefArticles.Count > 0)
        {
            lowArticles.Remove(briefArticles.First());
            mandatoryArticles.Add(briefArticles.First());
        }

        //letters are mandatory
        var letterArticles = highArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.articletype.ToLower() == "letters").ToList();
        if (letterArticles.Count > 0)
        {
            highArticles.Remove(letterArticles.First());
            mandatoryArticles.Add(letterArticles.First());
        }

        letterArticles.Clear();
        letterArticles = lowArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.articletype.ToLower() == "letters").ToList();
        if (letterArticles.Count > 0)
        {
            lowArticles.Remove(letterArticles.First());
            mandatoryArticles.Add(letterArticles.First());
        }

        var jumpArticles = lowArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.isjumparticle).ToList();
        if (jumpArticles.Count > 0)
        {
            mandatoryArticles.AddRange(lowArticles.Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.isjumparticle).ToList());
            lowArticles.RemoveAll(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && x.isjumparticle);

        }

        mandatoryArticles = mandatoryArticles.OrderByDescending(x => x.articletype.ToLower() == "briefs" || x.articletype.ToLower() == "letters").ThenByDescending(x => x.priority).ToList();

        foreach (var mandatoryArticle in mandatoryArticles)
        {
            var clonedArticle = Helper.DeepCloneBox(mandatoryArticle);
            clonedArticle.isMandatory = true;
            int mandatoryArticleArea = Helper.BoxCanbeFittedInPage(remainingArea, pageInfo._availablelngth, clonedArticle);

            if (mandatoryArticleArea > 0)
            {
                boxes.availablearea = remainingArea - mandatoryArticleArea;
                remainingArea -= mandatoryArticleArea;
                boxes.boxlist.Add(clonedArticle);
            }
        }

        // Add remaining high-priority articles (priority 4)
        AddArticlesToBoxList(highArticles, 4, boxes, ref remainingArea, pageInfo);

        AddArticlesToBoxList(lowArticles, -1, boxes, ref remainingArea, pageInfo);

        ConsolidateLoftArticles(boxes);

        //if there is a brief article on the page, it will become mandatory, rest all becomes optional
        if (boxes.boxlist.Any(x => x.articletype.ToLower() == "briefs"))
        {
            //only briefs are mandatory
            boxes.boxlist.ForEach(x => x.isMandatory = x.articletype.ToLower() == "briefs");
        }

        //if there is a letter article on the page, it will become mandatory, rest all becomes optional
        if (boxes.boxlist.Any(x => x.articletype.ToLower() == "letters"))
        {
            //only letters are mandatory
            boxes.boxlist.ForEach(x => x.isMandatory = x.articletype.ToLower() == "letters");
        }

        return new List<BoxList> { boxes };
    }

    private void AddArticlesToBoxList(List<Box> articles, int priority, BoxList boxList, ref int remainingArea, PageInfo pageInfo)
    {
        var filteredArticles = articles
            .Where(x => x.page == pageInfo.pageid && x.pagesname == pageInfo.sname && (priority == -1 || x.priority == priority))
            .OrderByDescending(x => x.priority).ThenBy(x => x.rank);

        //bool isMandatoryArtice = !boxList.boxlist.Any(); //If zero A article on the page, make the highest priority items as mandatory item

        foreach (var article in filteredArticles)
        {
            int articleArea = Helper.BoxCanbeFittedInPage(remainingArea, pageInfo._availablelngth, article);
            //FLOW-542:If there is not any A article, all the articles will be optional
            //article.isMandatory = isMandatoryArtice;

            //POK: Make the Jump article as mandatory
            if (article.isjumparticle)
                article.isMandatory = true;

            if (articleArea > 0)
            {
                boxList.availablearea -= articleArea;
                remainingArea -= articleArea;
                boxList.boxlist.Add(article);
            }
        }
    }
    //private void AddArticlesToBoxListNew(List<Box> articles, int priority, BoxList boxList, ref int remainingArea, PageInfo pageInfo)
    //{
    //    var filteredArticles = articles
    //        .Where(x => x.page == pageInfo.pageid && (priority == -1 || x.priority == priority))
    //        .OrderByDescending(x=>x.priority).ThenBy(x => x.rank);

    //    bool isMandatoryArtice = !boxList.boxlist.Any(); //If zero A article on the page, make the highest priority items as mandatory item

    //    foreach (var article in filteredArticles)
    //    {
    //        int articleArea = Helper.BoxCanbeFittedInPage(remainingArea, pageInfo._availablelngth, article);
    //        if (priority>3)
    //            article.isMandatory = isMandatoryArtice;

    //        if (articleArea > 0 )
    //        {
    //            boxList.availablearea -= articleArea;
    //            remainingArea -= articleArea;
    //            boxList.boxlist.Add(article);
    //        }
    //    }
    //}

    private void ConsolidateLoftArticles(BoxList boxList)
    {
        var loftArticles = boxList.boxlist.Where(x => x.articletype?.Equals("loft", StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (loftArticles.Count > 1)
        {
            string retainedLoftArticleId = loftArticles.First().Id;
            boxList.boxlist.RemoveAll(x => x.articletype?.Equals("loft", StringComparison.OrdinalIgnoreCase) == true && x.Id != retainedLoftArticleId);
        }
    }


    private List<BoxList> FindMaxHighPriorityItems_MandatoryOrder(List<Box> higharticles, List<Box> lowarticles, PageInfo _pinfo, string _section, int _mandatoryOrder)
    {

        List<List<Box>> finalList = new List<List<Box>>();
        List<BoxList> _boxlist = new List<BoxList>();

        List<Box> _barticles = new List<Box>();
        bool bloftadded = false;
        int _remavailablearea = _pinfo._availablepagearea;
        var _tarticle = Helper.DeepCloneBox(higharticles.Where(x => x.page == _pinfo.pageid && x.priority == 5).ToList()[0]);
        _tarticle.isMandatory = true;

        int _tarea = Helper.BoxCanbeFittedInPage(_pinfo._availablepagearea, _pinfo._availablelngth, _tarticle);
        if (_tarea > 0)
        {
            BoxList _list = new BoxList();
            _list.availablearea = _pinfo._availablepagearea - _tarea;
            _remavailablearea = _pinfo._availablepagearea - _tarea;
            _list.boxlist.Add(_tarticle);
            _boxlist.Add(_list);
        }
        else
            return _boxlist;

        //Add the remaining articles
        foreach (var item in higharticles.Where(x => x.page == _pinfo.pageid && x.priority == 4).OrderByDescending(x => -1 * x.rank))
        {
            int _area = 0;
            _area = Helper.BoxCanbeFittedInPage(_remavailablearea, _pinfo._availablelngth, item);
            if (_area > 0)
            {
                BoxList _list = _boxlist[0];
                _list.availablearea = _remavailablearea - _area;
                _remavailablearea = _list.availablearea;
                _list.boxlist.Add(item);
            }
        }

        //Add the remaining articles
        foreach (var item in lowarticles.Where(x => x.page == _pinfo.pageid).OrderByDescending(x => -1 * x.rank))
        {
            int _area = 0;
            _area = Helper.BoxCanbeFittedInPage(_remavailablearea, _pinfo._availablelngth, item);
            if (_area > 0)
            {
                BoxList _list = _boxlist[0];
                _list.availablearea = _remavailablearea - _area;
                _remavailablearea = _list.availablearea;
                _list.boxlist.Add(item);
            }
        }

        if (_boxlist[0].boxlist.Count(c => c.articletype != null && c.articletype.ToLower().Equals("loft")) > 1)
        {
            string _loftarticleid = _boxlist[0].boxlist.Find(x => x.articletype.ToLower().Equals("loft")).Id;
            _boxlist[0].boxlist.RemoveAll(x => x.articletype.ToLower().Equals("loft") && x.Id != _loftarticleid);
        }
        return _boxlist;
    }
    private List<BoxList> FindMaxHighPriorityItems_V1(int _totalpages, int _pagenum, List<Box> higharticles, List<Box> lowarticles, PageInfo _pinfo, string _section, int _totalpagearea)
    {

        List<List<Box>> finalList = new List<List<Box>>();
        List<BoxList> _boxlist = new List<BoxList>();

        int MAXARTICLES = 4;
        int MAXARTICLEPERMUTATION = 100;

        double _maxarticles = ((double)_pinfo._pagearea * (higharticles.Count + lowarticles.Where(x => x.articletype.ToLower() == "briefs").Count())) / _totalpagearea;
        int _imaxarticles = (int)Math.Ceiling(_maxarticles);

        if (_imaxarticles > MAXARTICLES)
            _imaxarticles = MAXARTICLES;
        double _maxtotalarticles = ((double)_pinfo._pagearea * (higharticles.Count + lowarticles.Count)) / _totalpagearea;
        int _imaxtotalarticles = (int)Math.Ceiling(_maxtotalarticles);

        if (_imaxtotalarticles > MAXARTICLES)
            _imaxtotalarticles = MAXARTICLES;


        Console.WriteLine("Max Items for page: " + _pagenum + " is " + _imaxarticles);
        //List<Box> _aarticles = higharticles.Where(x => x.priority == 5).OrderByDescending(x => -1 * x.boxorderId).ToList();
        //List<Box> _barticles = higharticles.Where(x => x.priority == 4).OrderByDescending(x => -1 * x.boxorderId).ToList();
        List<Box> _aarticles = higharticles.OrderByDescending(x => x.priority).ThenByDescending(x => -1 * x.boxorderId).ToList();
        List<Box> _barticles = new List<Box>();
        //POK: 09/25/23 A priority articles should be spread according to their order in the list.
        /*foreach (var _article in _aarticles)
        {
            int _area = Helper.BoxCanbeFittedInPage(_pinfo._availablepagearea, _pinfo._availablelngth, _article);
            if (_area > 0)
            {
                BoxList _list = new BoxList();
                _list.availablearea = _pinfo._availablepagearea - _area;
                _list.boxlist.Add(_article);
                _boxlist.Add(_list);
            }
        }*/

        if (_aarticles.Count() > 0)
        {
            var _tarticle = Helper.DeepCloneBox(_aarticles[0]);
            _tarticle.isMandatory = true;
            int _tarea = Helper.BoxCanbeFittedInPage(_pinfo._availablepagearea, _pinfo._availablelngth, _tarticle);
            if (_tarea > 0)
            {
                BoxList _list = new BoxList();
                _list.availablearea = _pinfo._availablepagearea - _tarea;
                _list.boxlist.Add(_tarticle);
                _boxlist.Add(_list);
            }
        }
        List<BoxList> _tmpNewListBoxList = new List<BoxList>();
        int _remainingpages = _totalpages - _pagenum + 1;
        if (_aarticles.Count() > _remainingpages)
        {
            int _articlecounter = _remainingpages; //POK: 09/25/23 skipping the 1st articles
            while (true)
            {
                if (_articlecounter > _aarticles.Count())
                    break;

                for (int _j = 0; _j < _boxlist.Count(); _j++)
                {
                    List<BoxList> _tmpListBoxList = new List<BoxList>();
                    BoxList _list = _boxlist[_j];
                    if (_list.boxlist.Count() >= _imaxarticles)
                        continue;
                    for (int _i = _articlecounter; _i < _aarticles.Count(); _i++)
                    {
                        Box _article = _aarticles[_i];
                        bool _articlesalreadyexist = false;
                        foreach (var _box in _list.boxlist)
                        {
                            if (_box.boxorderId == _article.boxorderId)
                            {
                                _articlesalreadyexist = true;
                                break;
                            }
                        }
                        if (_articlesalreadyexist)
                            continue;

                        int _area = 0;
                        _area = Helper.BoxCanbeFittedInPage(_list.availablearea, _pinfo._availablelngth, _article);
                        if (_area > 0)
                        {
                            BoxList _tmpBoxList = new BoxList();
                            _tmpBoxList.availablearea = _list.availablearea - _area;
                            _tmpBoxList.boxlist.AddRange(_list.boxlist);
                            _tmpBoxList.boxlist.Add(_article);
                            _tmpListBoxList.Add(_tmpBoxList);
                        }
                    }

                    if (_tmpListBoxList.Count() > 0)
                    {
                        _boxlist.RemoveAt(_j);
                        _j--;
                        _tmpNewListBoxList.AddRange(_tmpListBoxList);
                    }
                }
                _boxlist.AddRange(_tmpNewListBoxList);
                _tmpNewListBoxList = new List<BoxList>();
                _articlecounter++;
            }
        }

        //As #of A articles < Pages, We will have to fill some pages with B (and lower priority) articles.

        if (_aarticles.Count() == 0)
        {
            foreach (var _article in _barticles)
            {
                int _area = Helper.BoxCanbeFittedInPage(_pinfo._availablepagearea, _pinfo._availablelngth, _article);
                if (_area > 0)
                {
                    BoxList _list = new BoxList();
                    _list.availablearea = _pinfo._availablepagearea - _area;
                    _list.boxlist.Add(_article);
                    _boxlist.Add(_list);
                }
            }
        }

        //We need to start filling the B-priority articles now.
        int _brticlecounter = 0;
        while (true)
        {
            if (_brticlecounter > _barticles.Count())
                break;

            for (int _j = 0; _j < _boxlist.Count(); _j++)
            {
                List<BoxList> _tmpListBoxList = new List<BoxList>();
                BoxList _list = _boxlist[_j];
                if (_list.boxlist.Count() >= _imaxarticles)
                    continue;
                for (int _i = _brticlecounter; _i < _barticles.Count(); _i++)
                {
                    Box _article = _barticles[_i];
                    bool _articlesalreadyexist = false;
                    foreach (var _box in _list.boxlist)
                    {
                        if (_box.boxorderId == _article.boxorderId)
                        {
                            _articlesalreadyexist = true;
                            break;
                        }
                    }
                    if (_articlesalreadyexist)
                        continue;

                    int _area = 0;
                    _area = Helper.BoxCanbeFittedInPage(_list.availablearea, _pinfo._availablelngth, _article);
                    if (_area > 0)
                    {
                        BoxList _tmpBoxList = new BoxList();
                        _tmpBoxList.availablearea = _list.availablearea - _area;
                        _tmpBoxList.boxlist.AddRange(_list.boxlist);
                        _tmpBoxList.boxlist.Add(_article);
                        _tmpListBoxList.Add(_tmpBoxList);
                    }
                }

                if (_tmpListBoxList.Count() > 0)
                {
                    _boxlist.RemoveAt(_j);
                    _j--;
                    _tmpNewListBoxList.AddRange(_tmpListBoxList);
                }
            }
            _boxlist.AddRange(_tmpNewListBoxList);
            _tmpNewListBoxList = new List<BoxList>();
            _brticlecounter++;
        }

        HashSet<BigInteger> _combsadded = new HashSet<BigInteger>();

        for (int _i = 0; _i < _boxlist.Count(); _i++)
        {
            BoxList _lst = _boxlist[_i];
            BigInteger _int = 0;
            foreach (var _x in _lst.boxlist)
                _int += _x.boxorderId;

            if (_combsadded.Contains(_int))
            {
                _boxlist.RemoveAt(_i);
                _i--;
            }
            else
            {
                _combsadded.Add(_int);
            }
        }

        //No high priority items left
        if (_aarticles.Count() == 0 && _barticles.Count() == 0)
        {
            foreach (var _article in lowarticles)
            {
                //POK: If no high priority articles, we don't have to reserve space for Iamges
                // int _area = Helper.BoxCanbeFittedInPage(_pinfo._availablepagearea, _pinfo._availablelngth, _article);
                int _area = Helper.BoxCanbeFittedInPage(_pinfo._pagearea, _pinfo._availablelngth, _article);
                if (_area > 0)
                {
                    BoxList _list = new BoxList();
                    _list.availablearea = _pinfo._pagearea - _area;
                    _list.boxlist.Add(_article);
                    _boxlist.Add(_list);
                }
            }
        }
        //We need to start filling the C-priority articles now.
        int _crticlecounter = 0;
        while (true)
        {
            if (_crticlecounter > lowarticles.Count())
                break;

            for (int _j = 0; _j < _boxlist.Count(); _j++)
            {
                List<BoxList> _tmpListBoxList = new List<BoxList>();
                BoxList _list = _boxlist[_j];
                if (_list.boxlist.Count() >= _imaxtotalarticles)
                    continue;
                for (int _i = _crticlecounter; _i < lowarticles.Count(); _i++)
                {
                    Box _article = lowarticles[_i];
                    bool _articlesalreadyexist = false;
                    foreach (var _box in _list.boxlist)
                    {
                        if (_box.boxorderId == _article.boxorderId)
                        {
                            _articlesalreadyexist = true;
                            break;
                        }
                    }
                    if (_articlesalreadyexist)
                        continue;

                    int _area = 0;
                    _area = Helper.BoxCanbeFittedInPage(_list.availablearea, _pinfo._availablelngth, _article);
                    if (_area > 0)
                    {
                        BoxList _tmpBoxList = new BoxList();
                        _tmpBoxList.availablearea = _list.availablearea - _area;
                        _tmpBoxList.boxlist.AddRange(_list.boxlist);
                        _tmpBoxList.boxlist.Add(_article);
                        _tmpListBoxList.Add(_tmpBoxList);
                    }
                }

                if (_tmpListBoxList.Count() > 0)
                {
                    _boxlist.RemoveAt(_j);
                    _j--;
                    _tmpNewListBoxList.AddRange(_tmpListBoxList);
                }
            }
            _boxlist.AddRange(_tmpNewListBoxList);
            _tmpNewListBoxList = new List<BoxList>();
            _crticlecounter++;
        }
        for (int _i = 0; _i < _boxlist.Count(); _i++)
        {
            if (_boxlist[_i].boxlist.Count() >= 5)
            {
                _boxlist.RemoveAt(_i);
                _i--;
            }
        }

        _combsadded = new HashSet<BigInteger>();

        for (int _i = 0; _i < _boxlist.Count(); _i++)
        {
            BoxList _lst = _boxlist[_i];
            BigInteger _int = 0;
            foreach (var _x in _lst.boxlist)
                _int += _x.boxorderId;

            if (_combsadded.Contains(_int))
            {
                _boxlist.RemoveAt(_i);
                _i--;
            }
            else
            {
                _combsadded.Add(_int);
            }
        }

        foreach (var _list in _boxlist)
        {
            if (_list.boxlist.Count(c => c.articletype != null && c.articletype.ToLower().Equals("loft")) > 1)
            {
                string _loftarticleid = _list.boxlist.Find(x => x.articletype.ToLower().Equals("loft")).Id;
                _list.boxlist.RemoveAll(x => x.articletype.ToLower().Equals("loft") && x.Id != _loftarticleid);
            }

            if (_list.boxlist.Count(c => c.articletype != null && c.articletype.ToLower().Equals("briefs")) > 1)
            {
                string _loftarticleid = _list.boxlist.Find(x => x.articletype.ToLower().Equals("briefs")).Id;
                _list.boxlist.RemoveAll(x => x.articletype.ToLower().Equals("briefs") && x.Id != _loftarticleid);
            }
        }

        if (_boxlist.Count > MAXARTICLEPERMUTATION)
            _boxlist.RemoveRange(MAXARTICLEPERMUTATION, _boxlist.Count - MAXARTICLEPERMUTATION);

        return _boxlist;

    }

    private void GetNextPage_V4_Thread(int _pagenum, PageInfo _pinfo, List<Box> _tList, int _totalpages, BigInteger _boxscore, int _currentrun1, int _mandatoryorder)
    {
        IPacker packer = null;
        bool placelowerpriorityInsidebar = false;

        bool isTopRightPacker = Helper.GetBestPacker(_pinfo) == FlowPacker.TopRight; //New rule for choosing best packer FLOW-457 : UM

        if (ModelSettings.placelowerpriorityarticleatleftorright && _tList.Exists(x => x.priority <= 3))
        {
            placelowerpriorityInsidebar = true;
        }

        if (ModelSettings.bCustomPlacementEnabled && _pinfo.placementrule.Length > 0)
        {
            if (_pinfo.placementrule == "right")
            {
                packer = new Packer_TopRight();
                _pinfo.startingx = canvasx;
                isTopRightPacker = true;
            }
            else
            {
                packer = new Packer();
                isTopRightPacker = false;
            }
        }
        else
        {
            if (isTopRightPacker)
            {
                packer = new Packer_TopRight();
                _pinfo.startingx = canvasx;
            }
            else
            {
                packer = new Packer();
            }
        }
        if (_tList.Exists(x => x.articletype.ToLower() == "briefs") && _pinfo.pageid % 2 == 1)
        {
            packer = new Packer_TopRight();
            _pinfo.startingx = canvasx;
            isTopRightPacker = true;
        }

        packer.containerWidth = canvasx;
        packer.containerLength = canvasz;

        Box _loftarticle = null;
        if (_tList.Exists(x => x.articletype != null && x.articletype.ToLower().Equals("loft")))
        {
            Box _b = _tList.Find(x => x.articletype.ToLower().Equals("loft"));
            _loftarticle = ((List<Box>)articlePermMap[_b.Id])[0];
        }

        StaticBox packingArea = Helper.GetEditorialSpace(_pinfo); //This will return an static object of type editorial element
        if (packingArea == null)
        {
            Log.Information("Unable to find editorial space for page {id}", _pinfo.pageid);
            return;
        }


        string packerName = "LEFT";
        if (isTopRightPacker == true) packerName = "RIGHT";
        Log.Information("Packer for pageId = {id} is  {name}", _pinfo.pageid, packerName);

        Helper.AdjustSpacingBetweenElements(_pinfo, packingArea);

        _pinfo.pageEditorialArea.X = _pinfo.startingx = (!isTopRightPacker) ? packingArea.X : packingArea.X + packingArea.Width;
        _pinfo.pageEditorialArea.Y = _pinfo.startingy = packingArea.Y;
        _pinfo.pageEditorialArea.Width = packer.containerWidth = packingArea.Width;
        _pinfo.pageEditorialArea.Height = packer.containerLength = packingArea.Height;

        Log.Information("Editorial space for pageId = {id} | x={x},y={y},W={w},H={h}", _pinfo.pageid, _pinfo.pageEditorialArea.X, _pinfo.pageEditorialArea.Y, _pinfo.pageEditorialArea.Width, _pinfo.pageEditorialArea.Height);

        if (_loftarticle != null)
        {
            _loftarticle.pos_z = _pinfo.sectionheaderheight;
            _loftarticle.position.pos_z = _pinfo.sectionheaderheight;
            if (_pinfo.staticBoxes != null && _pinfo.staticBoxes.Count > 0)
            {
                var adsAndFooterList = _pinfo.staticBoxes.Where(x => x.Type != FlowElements.Header).ToList();
                foreach (var sBox in adsAndFooterList)
                {
                    if (Helper.AreIntersectedWidthStaticBox(_loftarticle, sBox))
                    {
                        _tList.RemoveAll(x => x.articletype.ToLower().Equals("loft"));
                        _loftarticle = null;
                        break;
                    }
                }
            }
        }
        if (_loftarticle != null)
        {
            _pinfo.startingy = _pinfo.sectionheaderheight + (int)_loftarticle.length + ModelSettings.articleseparatorheight;
            packer.containerLength = canvasz - _pinfo.startingy;
        }

        Dictionary<BigInteger, Int32> _tmpUniquIds = new Dictionary<BigInteger, Int32>();
        List<ScoreList> _tmpScoreList = new List<ScoreList>();
        List<List<Box>> listOfLists = new List<List<Box>>();
        int _numHighPriorityArticles = _tList.Where(x => x.priority >= 4).ToList().Count();

        Log.Information("Inside GetNextPage_V4_Thread: Run {run} at {time}", _currentrun1, DateTime.Now);
        Log.Information("Value of placelowerpriorityInsidebar: {val}", placelowerpriorityInsidebar);
        List<List<Box>> result = new List<List<Box>>();
        if (!placelowerpriorityInsidebar)
            result = GetAllCrossJoin(_tList.Where(x => new string[] { "", "nfnd", "briefs", "letters", "picturelead" }.Contains(x.articletype.ToLower())).ToList(), _pinfo.ads.Count() > 0 ? true : false, _pinfo, _pinfo._availablepagearea);
        else
        {
            int _pagearea = _pinfo._availablepagearea;
            if (isTopRightPacker)
                _pagearea = _pinfo.paintedCanvas.Count(x => x.Value == FlowElements.Editorial && x.Key.Key >= packingArea.X + ModelSettings.sidebarWidth &&
                x.Key.Key < packingArea.X + packingArea.Width);
            else
                _pagearea = _pinfo.paintedCanvas.Count(x => x.Value == FlowElements.Editorial && x.Key.Key >= packingArea.X &&
                x.Key.Key < packingArea.X + packingArea.Width - ModelSettings.sidebarWidth);
            packer.containerWidth = packer.containerWidth - ModelSettings.sidebarWidth;
            result = GetAllCrossJoin(_tList.Where(x => new string[] { "", "nfnd", "briefs", "letters", "picturelead" }.Contains(x.articletype.ToLower())
            && x.priority >= 4).ToList(), _pinfo.ads.Count() > 0 ? true : false, _pinfo, _pagearea);
        }
        if (result == null || result.Count() == 0)
            return;

        List<Box> _packBox = new List<Box>();

        BigInteger mandatoryId = 0;
        List<Box> _tempBoxlist = _tList.Where(x => x.isMandatory == true).ToList();
        if (_tempBoxlist != null && _tempBoxlist.Count() > 0)
            mandatoryId = _tempBoxlist[0].boxorderId;

        int _counter = 0;
        Log.Information("PageId = {id} Start of Packer: {count}", _pinfo.pageid, result.Count);

        int _aarticleheight = 0;
        bool haspicturelead = false;
        int _adremswidth = 0;
        if (_pinfo.hasQuarterPageAds)
        {
            _aarticleheight = _pinfo.ads[0].newy - _pinfo.pageEditorialArea.Y - ModelSettings.minSpaceBetweenAdsAndStories;
            haspicturelead = _tList.Exists(x => x.articletype == "picturelead");
            _adremswidth = _pinfo.pageEditorialArea.Width - _pinfo.ads[0].newwidth;
        }

        unplacedReason.Clear();
        foreach (List<Box> _resultitem in result)
        {
            try
            {
                _packBox = new List<Box>();
                List<Box> _nresultitem = _resultitem.OrderByDescending(x => x.priority).ThenByDescending(x => x.length * x.width).ToList();
                int _maxwidth = 0;

                if (_pinfo.hasQuarterPageAds)
                {
                    Box _mainarticle;
                    if (haspicturelead)
                        _mainarticle = _resultitem.FirstOrDefault(x => x.articletype == "picturelead" && x.width > _adremswidth);
                    else
                        _mainarticle = _resultitem.FirstOrDefault(x => x.priority == 5 && x.width > _adremswidth);

                    if (_mainarticle == null)
                        continue;
                    if (_mainarticle != null && _mainarticle.length > _aarticleheight)
                        continue;

                    if (_mainarticle != null && _mainarticle.length < _aarticleheight)
                        _mainarticle.length = _aarticleheight;
                }

                foreach (var _boxitem in _resultitem)
                {
                    if (_boxitem.width > _maxwidth)
                        _maxwidth = (int)_boxitem.width;
                    Box _tempb = Helper.CustomCloneBox(_boxitem);
                    _packBox.Add(_tempb);
                }

                if (_pinfo.startingx > 0 && !isTopRightPacker)
                    if (!(_packBox.Exists(x => x.articletype.ToLower() == "briefs") && _pinfo.pageid % 2 == 1))
                        if (_maxwidth > canvasx - _pinfo.startingx)
                            continue;

                List<Box> boxes = packer.StartPacking(_packBox, 1, _pinfo);

                if (!CheckAllPriorityItemsPlaced(boxes, mandatoryId))
                    continue;

                BigInteger[] arrScores = Helper.calculateScore(boxes, true, _pinfo);
                int _newscore = (int)arrScores[1];
                BigInteger _newid = arrScores[0];

                if (!_tmpUniquIds.ContainsKey(_newid))
                {
                    _tmpUniquIds.Add(_newid, _newscore);
                    ScoreList score = new ScoreList(boxes.Where(x => x.position != null).ToList(), canvasx * canvasz);
                    score.finalscore = _newscore;
                    score.pageid = _pinfo.sname + _pinfo.pageid;
                    score.uniqueid = _newid;
                    _tmpScoreList.Add(score);
                }
                else
                {
                    if (_tmpUniquIds[_newid] < _newscore)
                    {
                        _tmpUniquIds[_newid] = _newscore;
                        _tmpScoreList.RemoveAll(x => x.uniqueid == _newid);
                        ScoreList score = new ScoreList(boxes.Where(x => x.position != null).ToList(), canvasx * canvasz);
                        score.finalscore = _newscore;
                        score.pageid = _pinfo.sname + _pinfo.pageid;
                        score.uniqueid = _newid;
                        _tmpScoreList.Add(score);
                    }
                }
                _counter++;
            }
            catch (Exception e)
            {
                throw;
            }

        }

        Log.Information("End of Packer: {count}", result.Count);
        
        LogArticlePlacementInfo(_pinfo, _tmpScoreList);

        //POK: As article placement started from top right, their x postion needs to be changed back relative to to top left
        if (isTopRightPacker || _tList.Exists(x => x.articletype.ToLower() == "briefs") && _pinfo.pageid % 2 == 1)
        {
            foreach (var _list in _tmpScoreList)
            {
                foreach (var _tbox in _list.boxes)
                    _tbox.position.pos_x = _tbox.position.pos_x - (int)_tbox.width;
            }
        }

        if (placelowerpriorityInsidebar)
        {
            Log.Information("Processing the sidebar");
            ScoreList _sidebarScoreList = null;
            int _prevarticlesAdded = 0, _currarticlesAdded = 0;
            int _previousscore = 0;
            int _pagearea = _pinfo._availablepagearea;
            if (isTopRightPacker)
                _pagearea = _pinfo.paintedCanvas.Count(x => x.Value == FlowElements.Editorial && x.Key.Key >= packingArea.X &&
                x.Key.Key < packingArea.X + ModelSettings.sidebarWidth);
            else
                _pagearea = _pinfo.paintedCanvas.Count(x => x.Value == FlowElements.Editorial && x.Key.Key >= packingArea.X + packingArea.Width - ModelSettings.sidebarWidth &&
                x.Key.Key < packingArea.X + packingArea.Width);

            Log.Information("Page area for the sidebar: {area}, {totalarea}", _pagearea, _pinfo._availablepagearea);
            result = GetAllCrossJoin(_tList.Where(x => x.priority <= 3).ToList(), _pinfo.ads.Count() > 0 ? true : false, _pinfo, _pagearea);

            IPacker sidepacker = new Packer();
            sidepacker.containerLength = canvasz - _pinfo.startingy;
            sidepacker.containerWidth = ModelSettings.sidebarWidth;
            if (isTopRightPacker)
            {
                _pinfo.startingx = packingArea.X;
            }
            else
            {
                _pinfo.startingx = packingArea.X + packingArea.Width - ModelSettings.sidebarWidth;
            }
            Log.Information("Start of Sidebar Packer: {count}", result.Count);
            foreach (List<Box> _resultitem in result)
            {
                try
                {
                    _packBox = Helper.CustomCloneListBox(_resultitem);

                    List<Box> boxes = sidepacker.StartPacking(_packBox, 1, _pinfo);
                    BigInteger[] arrScores = Helper.calculateScore(boxes, true, _pinfo);
                    int _newscore = (int)arrScores[1];
                    BigInteger _newid = arrScores[0];
                    _currarticlesAdded = boxes.Count(x => x.position != null);
                    if (_sidebarScoreList == null)
                    {
                        ScoreList score = new ScoreList(boxes.Where(x => x.position != null).ToList(), canvasx * canvasz);
                        score.finalscore = _newscore;
                        score.pageid = _pinfo.sname + _pinfo.pageid;
                        score.uniqueid = _newid;
                        _sidebarScoreList = score;
                        _prevarticlesAdded = _currarticlesAdded;
                        _previousscore = _newscore;
                    }
                    else
                    {
                        if (_newscore > _previousscore || _currarticlesAdded > _prevarticlesAdded)
                        {
                            ScoreList score = new ScoreList(boxes.Where(x => x.position != null).ToList(), canvasx * canvasz);
                            score.finalscore = _newscore;
                            score.uniqueid = _newid;
                            score.pageid = _pinfo.sname + _pinfo.pageid;
                            _sidebarScoreList = score;
                            _prevarticlesAdded = _currarticlesAdded;
                            _previousscore = _newscore;
                        }
                    }
                }
                catch (Exception e)
                {
                    throw;
                }

            }

            if (_sidebarScoreList == null)
                Log.Information("Didn't generate the score for the Sidebar");
            if (_sidebarScoreList != null)
            {
                for (int _p = 0; _p < _tmpScoreList.Count; _p++)
                {
                    List<Box> _lstbox = new List<Box>();
                    _lstbox.AddRange(_tmpScoreList[_p].boxes.Where(x => x.position != null));
                    _lstbox.AddRange(_sidebarScoreList.boxes);
                    BigInteger[] arrScores = Helper.calculateScore(_lstbox, true, _pinfo);
                    int _newscore = (int)arrScores[1];
                    BigInteger _newid = arrScores[0];
                    ScoreList score = new ScoreList(_lstbox, canvasx * canvasz);
                    score.finalscore = _newscore;
                    score.uniqueid = _newid;
                    score.pageid = _pinfo.sname + _pinfo.pageid;
                    _tmpScoreList.RemoveAt(_p);
                    _tmpScoreList.Insert(_p, score);
                }
            }
        }

        if (_loftarticle != null)
        {
            foreach (var _list in _tmpScoreList)
            {
                _list.boxes.Add(_loftarticle);
                BigInteger[] arrScores = Helper.calculateScore(_list.boxes, true, _pinfo);
                _list.finalscore = (int)arrScores[1];
                _list.uniqueid = arrScores[0];
            }
        }

        /*RP: We need to disable the adwrap only if all the below conditions are met:
         *    1. maxwhitespaceAllowedBeforeTriggerAdwrap setting is configured
         *    2. Multi story adwrap page
         *    3. Packer is able to place all the stories
         *    4. Best layout had more whitespace than configured
          */
        bool triggerAdWrap = true;
        try
        {
            if (ModelSettings.textwrapSettings.maxwhitespaceAllowedBeforeTriggerAdwrap > 0 && _tList.Count > 1 && _tmpScoreList != null && _tmpScoreList.Count > 0 && _tmpScoreList.Max(x => x.boxes.Count) == _tList.Count)
            {
                ScoreList _bestsc = _tmpScoreList.FirstOrDefault(x => x.boxes.Count == _tList.Count);
                int _totaleditorialArea = _pinfo.paintedCanvas.Count(x => x.Value == FlowElements.Editorial);
                int _articlearea = (int)_bestsc.boxes.Sum(x => x.width * x.length);
                if (_bestsc.boxes.Exists(x => x.position.pos_z + x.length < ModelSettings.canvasheight))
                    _articlearea += (int)_bestsc.boxes.Where(x => x.position.pos_z + x.length < ModelSettings.canvasheight).Sum(x => x.width) * ModelSettings.articleseparatorheight;

                Log.Information("EditorialArea, Article Area & Default is: {a}, {b}, {c}", _totaleditorialArea, _articlearea, ModelSettings.textwrapSettings.maxwhitespaceAllowedBeforeTriggerAdwrap);
                if (_totaleditorialArea - _articlearea < ModelSettings.textwrapSettings.maxwhitespaceAllowedBeforeTriggerAdwrap)
                {
                    Log.Information("Disable the adwrap for Page: {pid}, Editorial Area: {ed}, ArticleArea: {ae}", _pinfo.pageid, _totaleditorialArea, _articlearea);
                    triggerAdWrap = false;
                }
            }
            else
            {
                Log.Information("Didn't enter into If");
            }
        }
        catch (Exception e)
        {
            Log.Error("Error while calculating if adwrap needs to be disabled: {msg}", e.StackTrace);
        }

        if (triggerAdWrap && ModelSettings.bTextWrapEnabled && _tList.Count <= ModelSettings.textwrapSettings.maxStories
                && !_tList.Exists(x => x.articletype != null && x.articletype.Length > 0))
        {
            int minyleft = 0, minyright = 0;

            if (ModelSettings.textwrapSettings.removelinesbelowAd > 0)
                Helper.ConvertEditorialSpaceBelowAdToAd(ref _pinfo, ModelSettings.textwrapSettings.removelinesbelowAd);

            if (_pinfo.paintedCanvas.Count(x => x.Key.Key == packingArea.X && x.Value == FlowElements.Editorial) > 0)
                minyleft = _pinfo.paintedCanvas.Where(x => x.Key.Key == packingArea.X && x.Value == FlowElements.Editorial).Max(x => x.Key.Value);
            if (_pinfo.paintedCanvas.Count(x => x.Key.Key == packingArea.X + packingArea.Width - 1 && x.Value == FlowElements.Editorial) > 0)
                minyright = _pinfo.paintedCanvas.Where(x => x.Key.Key == packingArea.X + packingArea.Width - 1 && x.Value == FlowElements.Editorial).Max(x => x.Key.Value);

            if (minyleft <= 15 && minyleft > 0)
            {
                int minyfornoneditorial = _pinfo.paintedCanvas.Where(x => x.Key.Key == packingArea.X && x.Value != FlowElements.Editorial && x.Key.Value > minyleft).Min(x => x.Key.Value);
                int _newx = _pinfo.paintedCanvas.Where(x => x.Value == FlowElements.Editorial && x.Key.Value == minyfornoneditorial).Min(x => x.Key.Key);
                packingArea.Width = packingArea.Width - (_newx - packingArea.X);
                packingArea.X = _newx;
                if (_pinfo.paintedCanvas.Count(x => x.Key.Key == packingArea.X && x.Value == FlowElements.Editorial) > 0)
                    minyleft = _pinfo.paintedCanvas.Where(x => x.Key.Key == packingArea.X && x.Value == FlowElements.Editorial).Max(x => x.Key.Value);
            }
            if (minyright != minyleft)
            {
                int _newheight = Math.Max(minyright, minyleft) + 1 - (packingArea.Y);
                CustomArticleSizeWrapper oCAS = new CustomArticleSizeWrapper(_tList, _pinfo, packingArea.X, packingArea.Y, packingArea.Width, _newheight, headlines, kickersmap, minyleft > minyright ? "r" : "l", articlePermMap, packingArea.Height);
                ScoreList sc = oCAS.GenerateLayout();
                if (sc != null)
                {
                    BigInteger[] arrScores = Helper.calculateScore(sc.boxes, true, _pinfo);
                    sc.finalscore = (int)arrScores[1];

                    sc.pageid = _pinfo.sname + _pinfo.pageid;
                    sc.uniqueid = arrScores[0];
                    _tmpScoreList.Clear();
                    _tmpScoreList.Add(sc);
                }
            }
        }

        if (_pinfo.hasQuarterPageAds)
        {
            foreach (var _score in _tmpScoreList)
            {
                foreach (var _box in _score.boxes.Where(x => x.position != null && x.position.pos_z >= _pinfo.ads[0].newy))
                {
                    if (_pinfo.ads[0].newx == 0)
                    {
                        if (_box.position.pos_x == _pinfo.ads[0].newwidth)
                            _box.stretch = "left";
                    }
                    else
                    {
                        if (_box.position.pos_x + _box.width == _pinfo.ads[0].newx)
                            _box.stretch = "right";
                    }
                }
            }
        }

        _globalfulllist.AddRange(_tmpScoreList);

        try
        {
            if (_tmpScoreList != null)
            {
                foreach (var _list in _tmpScoreList)
                {
                    Log.Information("Result for the Result: {count}", _list.boxes != null ? _list.boxes.Count : 0);
                }
            }
        }
        catch (Exception e)
        { }
    }

    private void LogArticlePlacementInfo(PageInfo _pinfo, List<ScoreList> _tmpScoreList)
    {
        var inputArticles = _pinfo.articleList;
        if (_tmpScoreList != null && _tmpScoreList.Count > 0 && unplacedReason.Count > 0)
        {
            var bestScoreCheck = _tmpScoreList.OrderByDescending(y => y.finalscore).FirstOrDefault();
            if (bestScoreCheck != null && bestScoreCheck.boxes.Count == inputArticles.Count)
            {
                Log.Information("All articles are placed, targeted to page-id " + _pinfo.pageid);
                unplacedReason.Clear();
            }
            else
            {
                Log.Warning("Some article are not placed placed on the pageId " + _pinfo.pageid + " | possible reasons = ");
                string allReasons = "";
                foreach (var r in unplacedReason)
                {
                    allReasons = allReasons + r;
                }
                Log.Warning(allReasons);
            }
        }
    }

    private bool CheckAllPriorityItemsPlaced(List<Box> _boxes, BigInteger mandatoryid)
    {
        bool _retval = true;
        string reason = "Not specified";

        if (mandatoryid > 0)
        {
            if (_boxes.Exists(x => x.boxorderId == mandatoryid))
            {
                if (_boxes.Where(x => x.boxorderId == mandatoryid).First().position == null)
                {
                    _retval = false;
                    reason = "No enough space.";
                }
            }
            else
            {
                reason = "mandatoryid does not exist.";
                _retval = false;
            }
        }

        if (_boxes.Exists(x => x.articletype.ToLower() == "briefs"))
        {
            if (_boxes.Where(x => x.articletype.ToLower() == "briefs").First().position == null)
            {
                _retval = false;
                reason = "no enough space for brief article";
            }
        }
        if (_boxes.Exists(x => x.isjumparticle && x.position == null))
        {
            _retval = false;
            reason = "no enough space for jump article";
        }
        if(_retval == false)
        {
            if (enableLogginOfUnplacedBoxCombinations)
            {
                string bInfo = "";
                int b = 1;
                foreach (var box in _boxes)
                {
                    bInfo = bInfo + ", Box-" + b + " => " + box.width + "x" + box.length;
                    b++;
                }
                Log.Information("Not placed box combinations => " + bInfo + " | Reason = " + reason);
            }
            if (!unplacedReason.Contains(reason))
            {
                unplacedReason.Add(reason);
            }
        }
        return _retval;
    }

    private List<List<Box>> GetAllCrossJoin(List<Box> _tList, bool _hasAds, PageInfo _pinfo, int _availablearea)
    {
        _tList = _tList.OrderByDescending(x => x.isMandatory).ThenByDescending(x => x.priority).ThenBy(x => x.boxorderId).ToList();
        List<List<Box>> results = null;
        int totalcount = _tList.Count;
        while (totalcount>=1)
        {
            results = GetAllCrossJoin(_tList.GetRange(0,totalcount), _hasAds, _pinfo, _availablearea, true);
            if (results != null && results.Count > 0)
                break;
            totalcount--;
        }
        return results;
    }
    private List<List<Box>> GetAllCrossJoin(List<Box> _tList, bool _hasAds, PageInfo _pinfo, int _availablearea, bool customlist)
    {
        List<List<Box>> _crosslist = new List<List<Box>>();
        List<List<Box>> listOfLists = new List<List<Box>>();

        if (_availablearea <= 0)
            _availablearea = _pinfo._availablepagearea;
        if (ModelSettings.optimizeArticlePermutations && _tList.Count >= ModelSettings.minarticleForOptimization)
        {
            Dictionary<string, List<Box>> _dictArticlePermutations = new Dictionary<string, List<Box>>();
            foreach (Box _box in _tList)
            {
                List<Box> _tmplist = Helper.DeepCloneListBox((List<Box>)articlePermMap[_box.Id]);

                if (_pinfo.hasQuarterPageAds && _tList.Exists(x => x.articletype == "picturelead") && _box.priority == 5
                    && !_box.articletype.Equals("picturelead", StringComparison.OrdinalIgnoreCase))
                {
                    _tmplist.RemoveAll(x => x.width > ModelSettings.canvaswidth - _pinfo.ads[0].newwidth);
                }
                if (_box.priority >= 4 && ModelSettings.placelowerpriorityarticleatleftorright && _tList.Exists(x => x.priority <= 3))
                {
                    //_tmplist.RemoveAll(x => x.width > ModelSettings.canvaswidth - _pinfo.ads[0].newwidth);

                }
                _dictArticlePermutations.Add(_box.Id, _tmplist);
            }

            List<List<double>> _lstlstWidths = new List<List<double>>();
            if (!_hasAds)
            {
                foreach (var _boxitem in _tList)
                {
                    List<Box> _tmplist = _dictArticlePermutations[_boxitem.Id];
                    BoxSettings _setting = (BoxSettings)ModelSettings.boxsettings[_boxitem.priority];
                    if (_setting.excludeColumnForNonAdPages.Count() > 0)
                    {
                        foreach (var excludedwidth in _setting.excludeColumnForNonAdPages)
                            _tmplist.RemoveAll(x => x.width == excludedwidth);
                    }
                }
            }

            _tList = _tList.OrderByDescending(x => x.isMandatory).ThenByDescending(x => x.priority).ThenBy(x => x.boxorderId).ToList();

            foreach (var _boxitem in _tList)
            {
                List<Box> _tmplist = _dictArticlePermutations[_boxitem.Id];
                if (_tmplist.Count > 0)
                    _lstlstWidths.Add(_tmplist.Select(x => x.width).Distinct().ToList());
            }
            List<List<double>> _lstlstWidthCrossJoin = Helper.CrossJoin(_lstlstWidths);

            foreach (var _widthItem in _lstlstWidthCrossJoin)
            {
                //bool babort = false;
                //if (_tList[0].isMandatory)
                //{
                //    int _parentwidth = (int)_widthItem[0];
                //    for (int _i = 1; _i < _widthItem.Count; _i++)
                //    {
                //        if ((int)_widthItem[_i] > _parentwidth)
                //        {
                //            babort = true;
                //            break;
                //        }
                //    }
                //}

                //if (babort)
                //    continue;

                foreach (var _mandatoryitem in _dictArticlePermutations[_tList[0].Id].Where(x => x.width == _widthItem[0]))
                {
                    if (_crosslist.Count > ModelSettings.MaxPermutationsPerPage)
                        break;
                    foreach (var _list2item in _dictArticlePermutations[_tList[1].Id].Where(x => x.width == _widthItem[1]))
                    {
                        if (_crosslist.Count > ModelSettings.MaxPermutationsPerPage)
                            break;
                        foreach (var _list3item in _dictArticlePermutations[_tList[2].Id].Where(x => x.width == _widthItem[2]))
                        {
                            if (_crosslist.Count > ModelSettings.MaxPermutationsPerPage)
                                break;
                            listOfLists.Clear();
                            int _totalarea = 0;
                            _totalarea = (int)(_mandatoryitem.length * _mandatoryitem.width) + (int)(_list2item.length * _list2item.width) + (int)(_list3item.length * _list3item.width);
                            if (_totalarea > _pinfo._availablepagearea)
                                continue;

                            listOfLists.Add(new List<Box>() { _mandatoryitem });
                            listOfLists.Add(new List<Box>() { _list2item });
                            listOfLists.Add(new List<Box>() { _list3item });
                            /*if (_totalarea + (int)(_list3item.length * _list3item.width) <= _pinfo._availablepagearea)
                            {
                                _totalarea += (int)(_list3item.length * _list3item.width);
                                listOfLists.Add(new List<Box>() { _list3item });
                            }*/

                            for (int _i = 3; _i < _widthItem.Count; _i++)
                            {
                                int _area = _tList[_i]._possibleAreas[(int)_widthItem[_i]];
                                if (_totalarea + _area <= _pinfo._availablepagearea)
                                {
                                    _totalarea += _area;
                                    List<Box> _tmplist = _dictArticlePermutations[_tList[_i].Id];
                                    listOfLists.Add(_tmplist.Where(x => x.width == _widthItem[_i]).ToList());
                                }
                            }
                            if (listOfLists.Count >= 3)
                            {
                                List<List<Box>> _tempcrosslist = Helper.CrossJoin(listOfLists, _availablearea);
                                _crosslist.AddRange(_tempcrosslist);

                            }
                        }
                    }
                }

            }
        }
        else
        {
            foreach (var _boxitem in _tList)
            {
                //if (_boxitem.Id == "1636ae37-9d40-43f7-a938-71d1347ea94b")
                //    ((List<Box>)articlePermMap[_boxitem.Id]).RemoveAll(x => x.width < 6);
                List<Box> _tmplist = Helper.DeepCloneListBox((List<Box>)articlePermMap[_boxitem.Id]);
                if (!_hasAds)
                {
                    BoxSettings _setting = (BoxSettings)ModelSettings.boxsettings[_boxitem.priority];
                    if (_setting.excludeColumnForNonAdPages.Count() > 0)
                    {
                        foreach (var excludedwidth in _setting.excludeColumnForNonAdPages)
                            _tmplist.RemoveAll(x => x.width == excludedwidth);
                    }
                }
                if (_tmplist.Count > 0)
                    listOfLists.Add(_tmplist);
            }
            try
            {
                _crosslist = Helper.CrossJoin(listOfLists, _availablearea);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


        return _crosslist;
    }
    //private List<List<Box>> GetAllCrossJoin_STF(List<Box> _tList)
    //{
    //    List<List<Box>> result = new List<List<Box>>();
    //    List<List<Box>> listOfLists = new List<List<Box>>();

    //    if (_tList.Count() == 4)
    //    {


    //    }
    //    if (_tList.Count() == 3)
    //    {
    //        if (_tList[0].priority == 5 && _tList[1].priority == 4 && _tList[2].priority == 4)
    //        {
    //            List<List<Box>> _tmpresult = GetAllPermutations(_tList, new int[] { 4, 1, 3 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 3, 1 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 2, 2 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 4, 4 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 3, 1 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 1, 3 });
    //            result.AddRange(_tmpresult);
    //        }

    //        if (_tList[0].priority == 5 && _tList[1].priority == 5 && _tList[2].priority == 4)
    //        {
    //            List<List<Box>> _tmpresult = GetAllPermutations(_tList, new int[] { 4, 4, 4 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 3, 1 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 4, 1 });
    //            result.AddRange(_tmpresult);
    //        }

    //        if (_tList[0].priority == 4 && _tList[1].priority == 4 && _tList[2].priority == 4)
    //        {
    //            List<List<Box>> _tmpresult = GetAllPermutations(_tList, new int[] { 4, 4, 4 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 3, 1 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 1, 3 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 4, 2, 2 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 4, 1 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 1, 4 });
    //            result.AddRange(_tmpresult);

    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 2, 2, 4 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 2, 4, 2 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 1, 3, 4 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 1, 4, 3 });
    //            result.AddRange(_tmpresult);

    //        }

    //        if (_tList[0].priority == 5 && _tList[1].priority == 5 && _tList[2].priority == 3)
    //        {
    //            List<List<Box>> _tmpresult = GetAllPermutations(_tList, new int[] { 4, 4, 0 });
    //            result.AddRange(_tmpresult);
    //            _tmpresult = null;
    //            _tmpresult = GetAllPermutations(_tList, new int[] { 3, 3, 1 });
    //            result.AddRange(_tmpresult);
    //        }
    //    }

    //    if (_tList.Count() != 3 || result.Count() == 0)
    //    {
    //        foreach (var _boxitem in _tList)
    //        {
    //            List<Box> _tmplist = ((List<Box>)articlePermMap[_boxitem.Id]).ToList(); //FindAllArticlePermutations(_boxitem);
    //            if (_tList.Count() == 2)
    //            {
    //                if (_tList[0].priority == 5 && _tList[1].priority == 5)
    //                    _tmplist.RemoveAll(x => x.width == 3);
    //                if (_tList[0].priority == 5 && _tList[1].priority == 4)
    //                    _tmplist.RemoveAll(x => x.width <= 3 && x.priority == 4);
    //            }

    //            if (_tmplist.Count() == 0)
    //                return null;
    //            listOfLists.Add(_tmplist);
    //        }

    //        result = Helper.CrossJoin(listOfLists);
    //    }
    //    return result;
    //}

    //private List<List<Box>> GetAllPermutations(List<Box> _tList, int[] _values)
    //{
    //    List<List<Box>> result;
    //    List<Box> _tmplist = ((List<Box>)articlePermMap[_tList[0].Id]).ToList();
    //    List<Box> _tmplist1 = ((List<Box>)articlePermMap[_tList[1].Id]).ToList();
    //    List<Box> _tmplist2 = ((List<Box>)articlePermMap[_tList[2].Id]).ToList();

    //    _tmplist = _tmplist.Where(x => x.width == _values[0]).ToList();
    //    _tmplist1 = _tmplist1.Where(x => x.width == _values[1]).ToList();
    //    _tmplist2 = _tmplist2.Where(x => x.width == _values[2]).ToList();
    //    List<List<Box>> listOfLists = new List<List<Box>>();
    //    listOfLists.Add(_tmplist);
    //    listOfLists.Add(_tmplist1);

    //    if (_tmplist2.Count > 0) //Excluding the C priorirty iterations for both the A items have length = 4
    //        listOfLists.Add(_tmplist2);


    //    result = Helper.CrossJoin(listOfLists);

    //    return result;

    //}

    private void GenerateJsonFile()
    {
        var lstAutomationPages = new List<AutomationPage>();
        int _pagestartingx = 0;

        for (int _i = 1; _i <= lstPages.Count(); _i++)
        {
            bool isdoubletruck = false;

            PageInfo _info = lstPages[_i - 1];
            isdoubletruck = _info.doubletruckpage;

            //Skip the doubletruck page if its odd
            if (isdoubletruck)
                if (_info.pageid % 2 != 0)
                    continue;

            Log.Information("Print Page: {sname}{id}", _info.sname, _info.pageid);
            if (_info.footer != null)
                _pagestartingx = _info.footer.width;
            else
                _pagestartingx = 0;

            AutomationPage _page = new AutomationPage();
            _page.camera_ready = false;

            _page.double_truck = isdoubletruck;// false;
            _page.section_name = _info.section;
            _page.pageid = _info.sname + _info.pageid;
            _page.height = canvasz;// (int)_info.height;

            if (isdoubletruck && _info.pageid % 2 == 0)
                _page.width = 2 * canvasx;
            else
                _page.width = canvasx;
            _page.freespace = 0;// (newfinalscores[0].lstScores[_i - 1].totalarea - newfinalscores[0].lstScores[_i - 1].areafilled)*100/ newfinalscores[0].lstScores[_i - 1].totalarea;
            ArrayList _lstautomationpagearticles = new ArrayList();

            int _yfordeck = 0;

            if (_info.msLayout != null)
            {
                var article = GenerateMultispreadJson(_info.msLayout);
                AuditStory(_page, _info.msLayout.Article);
                _lstautomationpagearticles.Add(article);
            }

            if (_info.PictureStoryLayout != null)
            {
                var article = GeneratePictureStoryJson(_info.PictureStoryLayout, _yfordeck);
                _lstautomationpagearticles.Add(article);
            }


            if (_info.sclist != null && _info.sclist.boxes != null)
            {
                Log.Information("Article exists for {sname}{id}", _info.sname, _info.pageid);
                var _boxlist = _info.sclist.boxes.Where(x => x.position != null).ToList();

                foreach (var _box in _boxlist.OrderByDescending(x => canvasx - x.position.pos_x).ThenByDescending(y => canvasz - y.position.pos_z).ToList())
                {
                    Log.Information("Printing article {id}", _box.Id);

                    AuditStory(_page, _box);

                    //Generate the AutomationPageArticle for Front page Jump Articles
                    if (_box.articletype.Equals("jumps", StringComparison.OrdinalIgnoreCase))
                    {
                        AutomationPageArticle _pagearticle = GenerateJumpArticleJson(_box);
                        _lstautomationpagearticles.Add(_pagearticle);

                        continue;
                    }
                    if (_box.articletype.Equals("adwrap", StringComparison.OrdinalIgnoreCase))
                    {
                        AutomationPageArticle _pagearticle = GenerateJumpArticleJson(_box, "adwrap");
                        _lstautomationpagearticles.Add(_pagearticle);
                        continue;
                    }

                    if (_box.articletype.Equals("picturelead", StringComparison.OrdinalIgnoreCase))
                    {
                        AutomationPageArticle _pagearticle = GeneratePictureLeadJson(_box);
                        _lstautomationpagearticles.Add(_pagearticle);
                        continue;
                    }
                    _yfordeck = 0;
                    Box article = articles.Where(s => s.Id == _box.Id).ToList()[0];
                    int positionx = (int)(_box.position != null ? _box.position.pos_x : -1);
                    int positionz = (int)(_box.position != null ? _box.position.pos_z : -1);
                    int boxlength = (int)_box.length;
                    int boxwidth = (int)_box.width;
                    int headlength = (int)_box.headlinelength;
                    int headwidth = (int)_box.headlinewidth;
                    if (_box.articletype.Equals("jumps", StringComparison.OrdinalIgnoreCase))
                        headwidth = _box.headlinewidth;
                    int kickerlength = (int)_box.kickerlength;
                    int preamble = 1;// article.preamble>0?1:0;

                    if (positionx >= 0 && positionz >= 0)
                    {
                        _yfordeck = positionz;
                        AutomationPageArticle _pagearticle = new AutomationPageArticle();
                        _pagearticle.article_id = _box.Id;
                        _pagearticle.x = positionx;
                        _pagearticle.y = positionz;
                        _pagearticle.height = boxlength;
                        _pagearticle.width = boxwidth;
                        _pagearticle.slug = "";
                        _pagearticle.headline = _box.articletype.ToLower() == "briefs" ? "" : headlineMap.ContainsKey(_box.Id) ? headlineMap[_box.Id].ToString() : "";
                        if (_box.stretch != null)
                            _pagearticle.stretch = _box.stretch;

                        if (_box.articletype.ToLower() == "briefs")
                        {
                            _pagearticle.storyType = "brief";
                            //height needs to be adjusted according to bottom ad
                            //y-position needs to be adjusted in case of top ad
                            //what to do in case of mid ad
                            _pagearticle.height = positionz + boxlength > canvasz ? canvasz - positionz : boxlength;
                        }

                        ArrayList _pageitems = new ArrayList();

                        //Add kickers
                        if (kickerlength > 0)
                        {
                            AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
                            _kickeritem.x = positionx;
                            _kickeritem.y = positionz;
                            _kickeritem.width = boxwidth;
                            _kickeritem.height = kickerlength;
                            _kickeritem.type = "kicker";
                            _kickeritem.size = null;
                            _pageitems.Add(_kickeritem);

                            positionz = positionz + kickerlength;
                            _yfordeck += kickerlength;
                        }

                        int _imgcount = _box.usedImageList == null ? 0 : _box.usedImageList.Count;

                        //add headline
                        AutomationPageArticleHeadline _headlineitem = new AutomationPageArticleHeadline();
                        _headlineitem.x = positionx;
                        if (_box.articletype == "picturestories")
                        {
                            if (_box.usedImageList.Any(x => x.aboveHeadline))
                            {
                                var lastImages = Helper.GetLastImages(_box.usedImageList.Where(x => x.aboveHeadline == true).ToList()).OrderByDescending(y => y.position.pos_z + y.length).FirstOrDefault();
                                _headlineitem.y = positionz + lastImages.position.pos_z + lastImages.length;
                                _yfordeck = _headlineitem.y;
                            }
                            else
                            {
                                _headlineitem.y = positionz;
                            }
                        }
                        else
                        {
                            if (_imgcount > 0 && _box.usedImageList[0].aboveHeadline)
                            {
                                _headlineitem.y = positionz + _box.usedImageList[0].length;
                            }
                            else
                            {
                                _headlineitem.y = positionz;
                            }
                        }


                        if (isdoubletruck || _box.articletype.Equals("jumps", StringComparison.OrdinalIgnoreCase)
                            || _box.articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase))
                            _headlineitem.width = headwidth;
                        else
                            _headlineitem.width = boxwidth;

                        _headlineitem.height = headlength;
                        if (_box.isjumparticle && _box.jumpfrompageid.Length > 0)
                            _headlineitem.type = "jumpHeadline";
                        else
                            _headlineitem.type = "headline";
                        _headlineitem.size = _box.headlinecaption;

                        if (_box.articletype.ToLower() != "briefs" && headlength != 0)
                        {
                            _pageitems.Add(_headlineitem);
                        }

                        _yfordeck += headlength;

                        if (_box.articletype == "loft")
                        {
                            if (_box.usedImageList != null && _box.usedImageList.Count > 0)
                            {
                                _pageitems.Add(Helper.GetImageItem(_box.usedImageList[0], positionx + boxwidth - _box.usedImageList[0].width, positionz, _box.Id));
                            }
                        }

                        if (_box.articletype == "jumps")
                        {
                            if (_box.usedImageList != null && _box.usedImageList.Count > 0)
                            {
                                _pageitems.Add(Helper.GetImageItem(_box.usedImageList[0], positionx + _box.usedImageList[0].position.pos_x, positionz + _box.usedImageList[0].position.pos_z, _box.Id));
                            }
                        }

                        if (_box.articletype == "picturestories")
                        {
                            if (_box.usedImageList != null && _box.usedImageList.Count > 0)
                            {
                                foreach (var _image in _box.usedImageList)
                                {
                                    AutomationPageArticleItems pictureStoryItem;
                                    if (_image.aboveHeadline || _image.isHeadlineHeightIncluded)
                                    {
                                        pictureStoryItem = Helper.GetImageItem(_image, _image.position.pos_x, (positionz + _image.position.pos_z), _box.Id);
                                    }
                                    else
                                    {
                                        pictureStoryItem = Helper.GetImageItem(_image, _image.position.pos_x, (positionz + headlength + _image.position.pos_z), _box.Id);
                                    }

                                    if (_image.captionlength <= 0)
                                    {
                                        //Remove caption
                                        if (pictureStoryItem.caption != null)
                                        {
                                            pictureStoryItem.caption.width = -1;
                                            pictureStoryItem.caption.height = -1;
                                            pictureStoryItem.caption.x = -1;
                                            pictureStoryItem.caption.y = -1;
                                        }
                                    }
                                    _pageitems.Add(pictureStoryItem);
                                }
                            }
                        }

                        if (_imgcount > 0 && _box.articletype.Equals("letters", StringComparison.OrdinalIgnoreCase))
                        {
                            var letterImage = _box.usedImageList.First();
                            var letterImagePositionX = positionx;

                            if (ModelSettings.letterSettings.imageAlignment.Equals("center", StringComparison.OrdinalIgnoreCase))
                            {
                                letterImagePositionX += (boxwidth - letterImage.width) / 2;
                            }
                            else if (ModelSettings.letterSettings.imageAlignment.Equals("right", StringComparison.OrdinalIgnoreCase))
                            {
                                letterImagePositionX = letterImagePositionX + boxwidth - letterImage.width;
                            }

                            var letterImageItem = Helper.GetImageItem(letterImage, letterImagePositionX, positionz + headlength, _box.Id);
                            _pageitems.Add(letterImageItem);
                        }


                        if (_imgcount > 0 && (_box.articletype == "" || _box.articletype.ToLower() == "nfnd"))
                        {
                            List<Image> lstAboveImages = _box.usedImageList.Where(x => x.aboveHeadline == true).ToList();
                            if (lstAboveImages != null && lstAboveImages.Count() > 0)
                            {
                                Image _mainImg = lstAboveImages.Where(x => x.mainImage == 1).ToList()[0];
                                AutomationPageArticleItems _mainImgItem = Helper.GetImageItem(_mainImg, positionx, positionz, _box.Id);
                                _pageitems.Add(_mainImgItem);
                                _yfordeck += _mainImg.length;
                                if (isdoubletruck && _box.parentArticleId.Length == 0) //Main DT Article. Do not run this for related articles
                                {
                                    //List<Image> _timages = _box.usedImageList.Where(x => x.mainImage == 0 && x.position.pos_x == positionx).ToList();
                                    if (_box.usedImageList.Where(x => x.mainImage == 0 && x.position.pos_x == positionx).ToList().Count > 0)
                                    {
                                        Image _subImg = _box.usedImageList.Where(x => x.mainImage == 0 && x.position.pos_x == positionx).ToList()[0];
                                        _yfordeck += _subImg.length;
                                        _headlineitem.y = _headlineitem.y + _subImg.length;
                                    }
                                }
                                if (_mainImg.width == _box.width)
                                {
                                    int newx = positionx;
                                    int newy = positionz + _mainImg.length;
                                    List<Image> _lstSubImages = lstAboveImages.Where(x => x.mainImage == 0).ToList();
                                    if (_lstSubImages != null && _lstSubImages.Count() > 0)
                                    {
                                        foreach (Image img in _lstSubImages)
                                        {
                                            AutomationPageArticleItems _subImgItem = Helper.GetImageItem(img, newx, newy, _box.Id);
                                            _pageitems.Add(_subImgItem);
                                            newx += img.width;
                                        }
                                        _yfordeck += _lstSubImages[0].length;
                                        _headlineitem.y = _headlineitem.y + _lstSubImages[0].length;
                                    }
                                }
                                else //mainimage.width < box.width
                                {
                                    if (!isdoubletruck)
                                    {
                                        int newx = positionx + _mainImg.width;
                                        int newy = positionz;
                                        foreach (Image img in lstAboveImages.Where(x => x.mainImage == 0 && x.imagetype == "Image"))
                                        {
                                            AutomationPageArticleItems _subImgItem = Helper.GetImageItem(img, newx, newy, _box.Id);
                                            _pageitems.Add(_subImgItem);
                                            newy += img.length;
                                        }
                                        foreach (Image img in lstAboveImages.Where(x => x.mainImage == 0 && x.imagetype != "Image"))
                                        {
                                            AutomationPageArticleItems _subImgItem = Helper.GetImageItem(img, newx, newy, _box.Id);
                                            _pageitems.Add(_subImgItem);
                                            newy += img.length;
                                        }

                                        //POK: Extending the length of subimage if total length of sub images != Main Image length
                                        int _subimagelength = 0;
                                        foreach (Image img in lstAboveImages.Where(x => x.mainImage == 0))
                                            _subimagelength += img.length;

                                        if (_subimagelength < _mainImg.length)
                                        {
                                            AutomationPageArticleItems _item = (AutomationPageArticleItems)_pageitems[_pageitems.Count - 1];
                                            _item.caption.height = _item.caption.height + _mainImg.length - _subimagelength;
                                        }
                                    }
                                    else
                                    {
                                        foreach (Image img in lstAboveImages.Where(x => x.mainImage == 0))
                                        {
                                            AutomationPageArticleItems _subImgItem;
                                            if (img.position == null)
                                                _subImgItem = Helper.GetImageItem(img, positionx + img.relativex, positionz + img.relativey, _box.Id);
                                            else
                                                _subImgItem = Helper.GetImageItem(img, img.position.pos_x, img.position.pos_z + kickerlength, _box.Id);

                                            _pageitems.Add(_subImgItem);
                                        }
                                    }
                                }
                            }

                            List<Image> lstBelowImages = _box.usedImageList.Where(x => x.aboveHeadline == false && x.width != _box.width).ToList();
                            int _remaininglength = (int)_box.length + _box.position.pos_z - _yfordeck;


                            var mugshots = lstBelowImages.Where(x => x.imagetype == "mugshot").ToList();
                            lstBelowImages.RemoveAll(x => x.imagetype == "mugshot");

                            //place all mugshots starting from after preamble
                            if (mugshots.Count > 0)
                            {
                                for (int i = 0; i < mugshots.Count; i++)
                                {
                                    var muhshotItem = Helper.GetImageItemD(mugshots[i], positionx + preamble + i, _yfordeck, _box.Id);
                                    _pageitems.Add(muhshotItem);
                                }
                            }

                            List<Image> lstFullWidthImages = _box.usedImageList.Where(x => x.aboveHeadline == false && x.width == _box.width).ToList();
                            if (lstFullWidthImages != null && lstFullWidthImages.Count() > 0)
                            {
                                foreach (var _img in lstFullWidthImages)
                                {
                                    AutomationPageArticleItems _subitem = _subitem = Helper.GetImageItem(_img, positionx + _img.relativex, _box.position.pos_z + (int)_box.length + _img.relativey, _box.Id);
                                    _pageitems.Add(_subitem);
                                }
                            }

                            if (lstBelowImages != null && lstBelowImages.Count() > 0)
                            {
                                List<Image> lstimgtypes = lstBelowImages.Where(x => x.imagetype == "Image").ToList();
                                List<Image> lstnonimgtypes = lstBelowImages.Where(x => x.imagetype != "Image" && x.imagetype != "mugshot").ToList();
                                List<Image> lstfacts = lstBelowImages.Where(x => x.imagetype == "FactBox").ToList();
                                int xOffsetIfMugshotsExists = mugshots.Count;

                                bool _multifacts = false;
                                if (lstfacts != null && lstfacts.Count() > 1)
                                    _multifacts = true;
                                if (isdoubletruck && _box.parentArticleId.Count() == 0) //main DT article
                                {
                                    foreach (var _img in lstBelowImages)
                                    {
                                        // AutomationPageArticleItems _tpai = Helper.GetImageItem(_img, _img.position.pos_x, _img.position.pos_z+ kickerlength, _box.Id);
                                        AutomationPageArticleItems _tpai = Helper.GetImageItem(_img, _img.position.pos_x, _img.position.pos_z, _box.Id);
                                        if (_tpai.x == 0 && _tpai.y == _yfordeck)
                                        {
                                            _yfordeck += _tpai.height + _tpai.caption.height;
                                        }
                                        _pageitems.Add(_tpai);
                                    }
                                }

                                if (_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    foreach (var _img in lstBelowImages)
                                    {
                                        AutomationPageArticleItems _tpai = Helper.GetImageItem(_img, positionx + _img.relativex, _yfordeck + _img.relativey, _box.Id);
                                        _pageitems.Add(_tpai);
                                    }
                                }
                                //FLOW-316
                                else if ((lstBelowImages.Count(x => x.position != null) == lstBelowImages.Count()) && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    foreach (var _img in lstBelowImages)
                                    {
                                        AutomationPageArticleItems _tpai = Helper.GetImageItem(_img, _img.position.pos_x, _img.position.pos_z, _box.Id);
                                        _pageitems.Add(_tpai);
                                    }
                                }
                                else if (lstBelowImages.Count() == 1 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    Image img1 = lstBelowImages[0];
                                    AutomationPageArticleItems _subImgItem1 = null;

                                    if (img1.imagetype.ToLower().Equals("image") && _remaininglength - img1.length < ModelSettings.minimumlinesunderImage)
                                    {
                                        img1.length = img1.length + (_remaininglength - img1.length);
                                    }

                                    if (_remaininglength - img1.length == 0)
                                    {
                                        _subImgItem1 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                    }
                                    else if (img1 != null && img1.width == _box.width)
                                    {
                                        _subImgItem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                    }
                                    else
                                    {
                                        var preambleAndMugshotCol = preamble + xOffsetIfMugshotsExists;
                                        int xpos = positionx + preambleAndMugshotCol;
                                        if (_remaininglength - img1.length == 0)
                                        {
                                            xpos = positionx + boxwidth - img1.width;
                                        }
                                        else
                                        {
                                            int _gap = 0;

                                            if (ModelSettings.placeImageAtCenter)
                                                _gap = (int)Math.Floor(((double)(boxwidth - preambleAndMugshotCol - img1.width)) / 2);
                                            else
                                                _gap = (int)Math.Ceiling(((double)(boxwidth - preambleAndMugshotCol - img1.width)) / 2);
                                            if (_gap > boxwidth - preambleAndMugshotCol - img1.width)
                                                _gap = 0;
                                            xpos = positionx + preambleAndMugshotCol + _gap;

                                        }
                                        _subImgItem1 = Helper.GetImageItem(img1, xpos, _yfordeck, _box.Id);
                                    }

                                    _pageitems.Add(_subImgItem1);
                                }
                                else if (lstBelowImages.Count() == 2 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    //if both are sub images
                                    if (lstimgtypes.Count() == 2)
                                    {
                                        Image img1 = lstimgtypes[0];
                                        Image img2 = lstimgtypes[1];
                                        if (img1.width == boxwidth && img2.width == boxwidth)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img2.relativey, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (img1.width == boxwidth && img2.width < boxwidth)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (img1.width < boxwidth && img2.width == boxwidth)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img2.relativey, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (_remaininglength - img1.length == 0 && _remaininglength - img2.length == 0)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img1.width - img2.width, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (_remaininglength - img1.length == 0)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (_remaininglength - img2.length == 0)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else
                                        {
                                            if (!ModelSettings.placeImageAtCenter)
                                            {
                                                if (img1.length < img2.length)
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                                else
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img2, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                            }
                                            else
                                            {
                                                if (img1.length >= img2.length)
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                                else
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img2, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Image img1 = null;
                                        Image img2 = null;
                                        if (lstimgtypes.Count() == 1)
                                        {
                                            img1 = lstimgtypes[0];
                                            img2 = lstnonimgtypes[0];
                                        }
                                        else
                                        {
                                            img1 = lstnonimgtypes[0];
                                            img2 = lstnonimgtypes[1];
                                        }

                                        if (img1.imagetype == "Image" && img1.width == boxwidth)
                                        {
                                            AutomationPageArticleItems _subitem1 = null;
                                            AutomationPageArticleItems _subitem2 = null;
                                            _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                            _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (img1.imagetype == "Image" && ModelSettings.placeImageAtCenter && _remaininglength < img1.length)
                                        {
                                            AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else if (xOffsetIfMugshotsExists + (img1.width + img2.width) < boxwidth) //place images side by side
                                        {
                                            AutomationPageArticleItems _subitem1 = null;
                                            AutomationPageArticleItems _subitem2 = null;
                                            if (img1.length >= img2.length)
                                            {
                                                _subitem1 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                                if (img2.length == boxlength)
                                                    _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img1.width - img2.width, _yfordeck, _box.Id);
                                                else
                                                    _subitem2 = Helper.GetImageItem(img2, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);

                                            }
                                            else
                                            {
                                                _subitem1 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                                _subitem2 = Helper.GetImageItem(img1, positionx + preamble + xOffsetIfMugshotsExists, _yfordeck, _box.Id);
                                            }

                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                        }
                                        else //they need to be placed vertically
                                        {
                                            if (!ModelSettings.placeImageAtCenter || img1.imagetype != "Image" ||
                                                img1.length + img2.length > _remaininglength - ModelSettings.minimumlinesunderImage)
                                            {
                                                if (img1.width >= img2.width)
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = null;
                                                    if (img1.length + img2.length <= _remaininglength - ModelSettings.minimumlinesunderImage)
                                                        _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img1.width, _yfordeck + img1.length, _box.Id);
                                                    else
                                                        _subitem2 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck + img1.length, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                                else
                                                {
                                                    AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img2, positionx + boxwidth - img2.width, _yfordeck, _box.Id);
                                                    AutomationPageArticleItems _subitem2 = null;
                                                    if (img1.length + img2.length <= _remaininglength - ModelSettings.minimumlinesunderImage)
                                                        _subitem2 = Helper.GetImageItem(img1, positionx + boxwidth - img2.width, _yfordeck + img2.length, _box.Id);
                                                    else
                                                        _subitem2 = Helper.GetImageItem(img1, positionx + boxwidth - img1.width, _yfordeck + img2.length, _box.Id);
                                                    _pageitems.Add(_subitem1);
                                                    _pageitems.Add(_subitem2);
                                                }
                                            }
                                            else
                                            {

                                                AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + preamble, _yfordeck, _box.Id);
                                                AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + preamble, _yfordeck + img1.length, _box.Id);
                                                _pageitems.Add(_subitem1);
                                                _pageitems.Add(_subitem2);


                                            }
                                        }
                                    }

                                }
                                else if (lstBelowImages.Count() == 3 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    if (lstimgtypes.Count() == 3)
                                    {
                                        Image img1 = lstimgtypes[0];
                                        Image img2 = lstimgtypes[1];
                                        Image img3 = lstimgtypes[2];

                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);

                                        _pageitems.Add(_subitem1);
                                        _pageitems.Add(_subitem2);
                                        _pageitems.Add(_subitem3);

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }

                                    if (lstimgtypes.Count() == 2)
                                    {
                                        Image img1 = lstimgtypes[0];
                                        Image img2 = lstimgtypes[1];
                                        Image img3 = lstnonimgtypes[0];

                                        AutomationPageArticleItems _subitem1;
                                        AutomationPageArticleItems _subitem2;
                                        AutomationPageArticleItems _subitem3;

                                        if (img1.width == _box.width && img2.width == _box.width && img3.width == _box.width)
                                        {
                                            _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                            _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _box.position.pos_z + (int)_box.length + img2.relativey, _box.Id);
                                            _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _box.position.pos_z + (int)_box.length + img3.relativey, _box.Id);
                                        }
                                        else if (img1.width == _box.width || img2.width == _box.width)
                                        {
                                            if (img1.width == img2.width)
                                            {
                                                _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _box.position.pos_z + (int)_box.length + img1.relativey, _box.Id);
                                                _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _box.position.pos_z + (int)_box.length + img2.relativey, _box.Id);
                                                _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                            }
                                            else
                                            {
                                                Image fullWidthArticle = img1.width == _box.width ? img1 : img2;
                                                Image smallerArticle = fullWidthArticle == img1 ? img2 : img1;
                                                _subitem1 = Helper.GetImageItem(fullWidthArticle, positionx + fullWidthArticle.relativex, _box.position.pos_z + (int)_box.length + fullWidthArticle.relativey, _box.Id);
                                                _subitem2 = Helper.GetImageItem(smallerArticle, positionx + smallerArticle.relativex, _yfordeck, _box.Id);
                                                _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                            }
                                        }
                                        else
                                        {
                                            if (img1.relativex == 0 || img2.relativex == 0 || img3.relativex == 0)
                                                throw new Exception("Relative values were not set");

                                            _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck, _box.Id);
                                            _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                            _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        }

                                        if (_subitem1 != null && _subitem2 != null && _subitem3 != null)
                                        {
                                            _pageitems.Add(_subitem1);
                                            _pageitems.Add(_subitem2);
                                            _pageitems.Add(_subitem3);
                                        }
                                    }
                                    if (lstimgtypes.Count() == 1)
                                    {
                                        Image img1 = lstimgtypes[0];
                                        Image img2 = lstnonimgtypes[0];
                                        Image img3 = lstnonimgtypes[1];
                                        if (img1.relativex == 0 || img2.relativex == 0 || img3.relativex == 0)
                                            throw new Exception("Relative values were not set");

                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        _pageitems.Add(_subitem1);
                                        _pageitems.Add(_subitem2);
                                        _pageitems.Add(_subitem3);
                                    }
                                }
                                else if (lstBelowImages.Count() == 4 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    if (lstimgtypes.Count() == 4)
                                    {
                                        var img1 = lstimgtypes[0];
                                        var img2 = lstimgtypes[1];
                                        var img3 = lstimgtypes[2];
                                        var img4 = lstimgtypes[3];


                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem4 = Helper.GetImageItem(img4, positionx + img4.relativex, _yfordeck + img4.relativey, _box.Id);
                                        _pageitems.AddRange(new List<AutomationPageArticleItems> { _subitem1, _subitem2, _subitem3, _subitem4 });

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }

                                    if (lstimgtypes.Count() == 3)
                                    {
                                        var img1 = lstimgtypes[0];
                                        var img2 = lstimgtypes[1];
                                        var img3 = lstimgtypes[2];
                                        var fq1 = lstnonimgtypes[0];


                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem4 = Helper.GetImageItem(fq1, positionx + fq1.relativex, _yfordeck + fq1.relativey, _box.Id);
                                        _pageitems.AddRange(new List<AutomationPageArticleItems> { _subitem1, _subitem2, _subitem3, _subitem4 });

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }
                                }
                                else if (lstBelowImages.Count() == 5 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    if (lstimgtypes.Count() == 4)
                                    {
                                        var img1 = lstimgtypes[0];
                                        var img2 = lstimgtypes[1];
                                        var img3 = lstimgtypes[2];
                                        var img4 = lstimgtypes[3];
                                        var fq1 = lstnonimgtypes[0];


                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem4 = Helper.GetImageItem(img4, positionx + img4.relativex, _yfordeck + img4.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem5 = Helper.GetImageItem(fq1, positionx + fq1.relativex, _yfordeck + fq1.relativey, _box.Id);
                                        _pageitems.AddRange(new List<AutomationPageArticleItems> { _subitem1, _subitem2, _subitem3, _subitem4, _subitem5 });

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }

                                    if (lstimgtypes.Count() == 3)
                                    {
                                        var img1 = lstimgtypes[0];
                                        var img2 = lstimgtypes[1];
                                        var img3 = lstimgtypes[2];
                                        var fq1 = lstnonimgtypes[0];
                                        var fq2 = lstnonimgtypes[1];


                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem4 = Helper.GetImageItem(fq1, positionx + fq1.relativex, _yfordeck + fq1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem5 = Helper.GetImageItem(fq2, positionx + fq2.relativex, _yfordeck + fq2.relativey, _box.Id);
                                        _pageitems.AddRange(new List<AutomationPageArticleItems> { _subitem1, _subitem2, _subitem3, _subitem4, _subitem5 });

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }
                                }
                                else if (lstBelowImages.Count() == 6 && !_multifacts && (!isdoubletruck || _box.parentArticleId.Length > 0))
                                {
                                    if (lstimgtypes.Count() == 4)
                                    {
                                        var img1 = lstimgtypes[0];
                                        var img2 = lstimgtypes[1];
                                        var img3 = lstimgtypes[2];
                                        var img4 = lstimgtypes[3];
                                        var fq1 = lstnonimgtypes[0];
                                        var fq2 = lstnonimgtypes[1];

                                        AutomationPageArticleItems _subitem1 = Helper.GetImageItem(img1, positionx + img1.relativex, _yfordeck + img1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem2 = Helper.GetImageItem(img2, positionx + img2.relativex, _yfordeck + img2.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem3 = Helper.GetImageItem(img3, positionx + img3.relativex, _yfordeck + img3.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem4 = Helper.GetImageItem(img4, positionx + img4.relativex, _yfordeck + img4.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem5 = Helper.GetImageItem(fq1, positionx + fq1.relativex, _yfordeck + fq1.relativey, _box.Id);
                                        AutomationPageArticleItems _subitem6 = Helper.GetImageItem(fq2, positionx + fq2.relativex, _yfordeck + fq2.relativey, _box.Id);

                                        _pageitems.AddRange(new List<AutomationPageArticleItems> { _subitem1, _subitem2, _subitem3, _subitem4, _subitem5, _subitem6 });

                                        _yfordeck += lstBelowImages.Where(x => x.relativex == 0).Sum(x => x.length);
                                    }
                                }
                            }


                        }
                        //---------------------------------------------

                        //Check for the Image that are aligned to the bottom. For those images
                        //empty caption line needs to be removed
                        foreach (var _item in _pageitems)
                        {
                            if (_item is AutomationPageArticleItems)
                            {
                                AutomationPageArticleItems _aitem = (AutomationPageArticleItems)_item;
                                int _finalx = _aitem.x + _aitem.width;
                                int _finaly = _aitem.y + _aitem.height + _aitem.caption.height;
                                if (_aitem.type.ToLower().Equals("image") && !_aitem.factbox)
                                {
                                    //POK: EPSLN-64
                                    if (_finaly == _box.position.pos_z + boxlength && ModelSettings.extraimagecaptionline > 0 && _aitem.caption.height > 0)
                                    {
                                        int _delta = ModelSettings.extraimagecaptionline;
                                        _aitem.caption.y += _delta;
                                        _aitem.caption.height = _aitem.caption.height - ModelSettings.extraimagecaptionline;
                                        _aitem.height += _delta;
                                    }
                                }
                            }
                        }
                        if (_box.jumpFromlength > 0 && article.jumpTolength == 0)
                        {
                            AutomationPageArticleByJump _jump = new AutomationPageArticleByJump();
                            _jump.x = positionx;
                            _jump.y = _yfordeck;
                            _jump.width = _box.jumpFromwidth;
                            _jump.height = _box.jumpFromlength;
                            _jump.type = "jumpFrom";
                            _jump.fromToPageId = _box.jumpfrompageid;
                            _pageitems.Add(_jump);
                            _yfordeck += _box.jumpFromlength;
                        }
                        if (article.preamble > 0)
                        {
                            AutomationPageArticleDeck _deck = new AutomationPageArticleDeck() { x = positionx, width = 1 };
                            _deck.y = _yfordeck;
                            _deck.height = article.preamble;
                            _deck.type = "deck";
                            _pageitems.Add(_deck);
                        }
                        //Adding byline

                        if (article.byline > 0)
                        {
                            AutomationPageArticleByline _byline = new AutomationPageArticleByline() { x = positionx, width = 1 };
                            _byline.y = _yfordeck + article.preamble;
                            _byline.height = article.byline;
                            _byline.type = "byline";
                            _pageitems.Add(_byline);
                        }

                        _pagearticle.items = _pageitems;
                        _lstautomationpagearticles.Add(_pagearticle);

                    }

                }

            }

            if (_info.multiSpreadScoreList != null && _info.multiSpreadScoreList.boxes != null)
            {
                Log.Information("Multispread article exists for {sname}{id}", _info.sname, _info.pageid);

                var boxList = _info.multiSpreadScoreList.boxes.Where(x => x.position != null).ToList();

                foreach (var box in boxList)
                {
                    AuditStory(_page, box);
                    Box article = articles.Where(s => s.Id == box.Id).First();

                    _yfordeck = _info.sectionheaderheight;
                    AutomationPageArticle multispreadArticle = new AutomationPageArticle();

                    multispreadArticle.article_id = box.Id;
                    multispreadArticle.x = box.position.pos_x;
                    multispreadArticle.y = box.position.pos_z;
                    multispreadArticle.height = box.position.length;
                    multispreadArticle.width = box.position.width;
                    multispreadArticle.slug = "";
                    multispreadArticle.headline = headlineMap[multispreadArticle.article_id].ToString();

                    ArrayList pageItems = new ArrayList();

                    if (box.kickerlength > 0)
                    {
                        AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
                        _kickeritem.x = multispreadArticle.x;
                        _kickeritem.y = multispreadArticle.y;
                        _kickeritem.width = multispreadArticle.width;
                        _kickeritem.height = box.kickerlength;
                        _kickeritem.type = "kicker";
                        _kickeritem.size = null;

                        pageItems.Add(_kickeritem);

                        _yfordeck += box.kickerlength;
                    }

                    if (_info.isMultiSpreadFirstPage)
                    {
                        //add headline, preamble and byline
                        AutomationPageArticleHeadline headlineitem = new AutomationPageArticleHeadline();
                        headlineitem.x = multispreadArticle.x;
                        headlineitem.y = multispreadArticle.y + box.kickerlength;

                        headlineitem.width = box.headlinewidth;
                        headlineitem.height = box.headlinelength;
                        headlineitem.type = "headline";
                        headlineitem.size = box.headlinecaption;

                        pageItems.Add(headlineitem);

                        _yfordeck += box.headlinelength;

                    }

                    if (box.usedImageList != null && box.usedImageList.Count > 0)
                    {
                        foreach (var img in box.usedImageList)
                        {
                            AutomationPageArticleItems image = Helper.GetImageItem(img, img.position.pos_x, img.position.pos_z, box.Id);

                            //if image is at the bottom of the page, remove the extra caption line
                            if (img.position.pos_z + img.length == ModelSettings.canvasheight)
                            {
                                image.caption.y += ModelSettings.extraimagecaptionline;
                                image.caption.height -= ModelSettings.extraimagecaptionline;
                                image.height += ModelSettings.extraimagecaptionline;
                            }

                            if (image.x == 0 && image.y == _yfordeck)
                                _yfordeck += image.height + image.caption.height;

                            pageItems.Add(image);
                        }
                    }

                    if (_info.isMultiSpreadFirstPage && article.preamble > 0)
                    {
                        AutomationPageArticleDeck _deck = new AutomationPageArticleDeck() { x = 0, width = 1 };
                        _deck.y = _yfordeck;
                        _deck.height = article.preamble;
                        _deck.type = "deck";
                        pageItems.Add(_deck);
                    }

                    if (_info.isMultiSpreadFirstPage && article.byline > 0)
                    {
                        AutomationPageArticleByline _byline = new AutomationPageArticleByline() { x = 0, width = 1 };
                        _byline.y = _yfordeck + article.preamble;
                        _byline.height = article.byline;
                        _byline.type = "byline";
                        pageItems.Add(_byline);
                    }

                    multispreadArticle.items = pageItems;
                    _lstautomationpagearticles.Add(multispreadArticle);
                }
            }
            var editorials = fillers.Where(x => x.pageId == _info.pageid).ToList();
            foreach (var ads in editorials)
            {
                AutomationPageAd _adsitem = new AutomationPageAd();
                _adsitem.x = (int)ads.x;
                _adsitem.y = (int)Math.Round(ads.y);
                _adsitem.width = (int)ads.Width;
                _adsitem.height = (int)ads.Height;
                _adsitem.ad_id = ads.file;
                _adsitem.original_x = ads.original_x;
                _adsitem.original_y = ads.original_y;
                _adsitem.original_width = ads.original_Width;
                _adsitem.original_height = ads.original_Height;
                _adsitem.type = "editorialad";
                Log.Information("Filler {file} placed on page {id} at position {x} x {y}", ads.file, ads.pageId, ads.x, ads.y);
                _lstautomationpagearticles.Add(_adsitem);
            }
            foreach (PageAds _ads in _info.ads)
            {
                AutomationPageAd _adsitem = new AutomationPageAd();
                _adsitem.x = _ads.newx;
                _adsitem.y = _ads.newy;
                _adsitem.width = _ads.newwidth;
                _adsitem.height = _ads.newheight;
                _adsitem.ad_id = _ads.filename.Replace(".pdf", "");
                _adsitem.original_x = _ads.x;
                _adsitem.original_y = _ads.y;
                _adsitem.original_width = _ads.dx;
                _adsitem.original_height = _ads.dy;
                _adsitem.type = "externalad";
                _lstautomationpagearticles.Add(_adsitem);
            }
            if (isdoubletruck)
            {
                PageInfo _nextDTPage = lstPages[_i];
                foreach (PageAds _ads in _nextDTPage.ads)
                {
                    AutomationPageAd _adsitem = new AutomationPageAd();
                    _adsitem.x = _ads.newx + ModelSettings.canvaswidth;
                    _adsitem.y = _ads.newy;
                    _adsitem.width = _ads.newwidth;
                    _adsitem.height = _ads.newheight;
                    _adsitem.ad_id = _ads.filename.Replace(".pdf", "");
                    _adsitem.original_x = _ads.x;
                    _adsitem.original_y = _ads.y;
                    _adsitem.original_width = _ads.dx;
                    _adsitem.original_height = _ads.dy;
                    _adsitem.type = "externalad";
                    _lstautomationpagearticles.Add(_adsitem);
                }
            }
            if (_info.sectionheaderheight > 0)
            {
                AutomationPageArticle _sectionheader = new AutomationPageArticle();
                _sectionheader.article_id = _info.section;
                _sectionheader.x = 0;
                _sectionheader.y = 0;
                _sectionheader.height = _info.sectionheaderheight;
                _sectionheader.width = canvasx;
                _sectionheader.type = "sectionhead";
                _lstautomationpagearticles.Add(_sectionheader);
            }
            if (isdoubletruck && _info.sectionheaderheight > 0)
            {
                AutomationPageArticle _sectionheader2 = new AutomationPageArticle();
                _sectionheader2.article_id = _info.section;
                _sectionheader2.x = canvasx;
                _sectionheader2.y = 0;
                _sectionheader2.height = _info.sectionheaderheight;
                _sectionheader2.width = canvasx;
                _sectionheader2.type = "sectionhead";
                _lstautomationpagearticles.Add(_sectionheader2);
            }

            if (_info.footer != null)
            {
                AutomationPageArticle _sectionfoot = new AutomationPageArticle();
                _sectionfoot.article_id = _info.section;
                _sectionfoot.x = _info.footer.x;
                _sectionfoot.y = _info.footer.y;
                _sectionfoot.height = _info.footer.height;
                _sectionfoot.width = _info.footer.width;
                _sectionfoot.type = "sectionfooter";
                _lstautomationpagearticles.Add(_sectionfoot);
            }
            _page.items = _lstautomationpagearticles;
            lstAutomationPages.Add(_page);
        }

        var _automation = new PrintAutomation()
        {
            items = lstAutomationPages
        };

        JsonSerializerOptions o = new JsonSerializerOptions();
        o.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

        System.IO.FileStream stream = System.IO.File.Create(sOutputJsonFile);
        System.Text.Json.JsonSerializer.Serialize(stream, _automation, o);
        stream.Flush();
        stream.Close();

    }

    private void AuditStory(AutomationPage _page, Box? _box)
    {
        var auditStory = audit.StoryList.FirstOrDefault(x => x.ArticleId == _box.Id && x.PageNumber == "");
        if (auditStory != null)
        {
            auditStory.Placed = "yes";
            auditStory.PageNumber = _page.pageid;
        }
    }

    public void GenerateHostDetailsFile()
    {
        var hostName = Dns.GetHostName();
        var addresses = Dns.GetHostAddresses(hostName);

        var sb = new StringBuilder();

        sb.AppendLine($"Hostname: {hostName}");
        sb.AppendLine("IP Addresses:");

        foreach (var ip in addresses)
        {
            sb.AppendLine(ip.ToString());
        }

        File.WriteAllText(sHostDetailsFile, sb.ToString());
    }

    private object GenerateMultispreadJson(MultiSpreadLayout layout)
    {
        AutomationPageArticle article = new AutomationPageArticle();
        article.article_id = layout.Article.Id;
        article.x = layout.PageLayout.Container.X;
        article.y = layout.PageLayout.Container.Y;
        var _yfordeck = article.y;
        article.height = layout.PageLayout.Container.Height;
        article.width = layout.PageLayout.Container.Width;
        article.slug = "";
        article.headline = headlineMap.ContainsKey(layout.Article.Id) ? headlineMap[layout.Article.Id].ToString() : "";

        ArrayList _pageitems = new ArrayList();
        if (layout.PageLayout.Container.KickerHeight > 0)
        {
            AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
            _kickeritem.x = article.x;
            _kickeritem.y = article.y;
            _kickeritem.width = article.width;
            _kickeritem.height = layout.PageLayout.Container.KickerHeight;
            _yfordeck += _kickeritem.height;
            _kickeritem.type = "kicker";
            _kickeritem.size = null;
            _pageitems.Add(_kickeritem);
        }

        //add headline
        AutomationPageArticleHeadline _headlineitem = new AutomationPageArticleHeadline();
        _headlineitem.x = article.x;
        _headlineitem.y = _yfordeck;
        _headlineitem.width = layout.PageLayout.Container.Width;
        _headlineitem.height = layout.PageLayout.Container.HeadlineHeight;
        _headlineitem.type = "headline";
        _headlineitem.size = layout.PageLayout.Container.HeadlineSize;
        _yfordeck += _headlineitem.height;

        if (layout.PageLayout.IsFrontPage && _headlineitem.height > 0)
        {
            _pageitems.Add(_headlineitem);
        }

        var yForImages = _yfordeck;

        if (layout.PageLayout.Images != null && layout.PageLayout.Images.Count > 0)
        {
            layout.PageLayout.Images.ForEach(img =>
            {
                var imageItem = Helper.GetImageItem(img, img.relativex, yForImages + img.relativey, article.article_id);
                if (imageItem.caption != null && imageItem.caption.height <= 0)
                {
                    imageItem.caption.width = -1;
                    imageItem.caption.height = -1;
                    imageItem.caption.x = -1;
                    imageItem.caption.y = -1;
                }
                _pageitems.Add(imageItem);

                if (imageItem.x == _headlineitem.x && imageItem.y == _yfordeck)
                    _yfordeck += imageItem.height + (imageItem.caption.height <= 0 ? 0 : imageItem.caption.height);
            });
        }

        if (layout.PageLayout.IsFrontPage && layout.Article.preamble > 0)
        {
            AutomationPageArticleDeck _deck = new AutomationPageArticleDeck() { x = layout.PageLayout.Container.X, width = 1 };
            _deck.y = _yfordeck;
            _deck.height = layout.Article.preamble;
            _deck.type = "deck";
            _pageitems.Add(_deck);
        }
        //Adding byline

        if (layout.PageLayout.IsFrontPage && layout.Article.byline > 0)
        {
            AutomationPageArticleByline _byline = new AutomationPageArticleByline() { x = layout.PageLayout.Container.X, width = 1 };
            _byline.y = _yfordeck + layout.Article.preamble;
            _byline.height = layout.Article.byline;
            _byline.type = "byline";
            _pageitems.Add(_byline);
        }
        article.items = _pageitems;
        return article;
    }
    private AutomationPageArticle GeneratePictureStoryJson(PictureStoriesLayout layout, int _yfordeck)
    {
        AutomationPageArticle article = new AutomationPageArticle();
        article.article_id = layout.Article.Id;
        article.x = (int)layout.Article.pos_x;
        article.y = (int)layout.Article.pos_z;
        _yfordeck = article.y;
        article.height = (int)layout.Article.length;
        article.width = (int)layout.Article.width;
        article.slug = "";
        article.headline = headlineMap.ContainsKey(layout.Article.Id) ? headlineMap[layout.Article.Id].ToString() : "";

        ArrayList _pageitems = new ArrayList();
        if (layout.Article.kickerlength > 0)
        {
            AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
            _kickeritem.x = article.x;
            _kickeritem.y = article.y;
            _kickeritem.width = article.width;
            _kickeritem.height = layout.Article.kickerlength;
            _yfordeck += _kickeritem.height;
            _kickeritem.type = "kicker";
            _kickeritem.size = null;
            _pageitems.Add(_kickeritem);
        }

        //add headline
        AutomationPageArticleHeadline _headlineitem = new AutomationPageArticleHeadline();
        _headlineitem.x = article.x;
        _headlineitem.y = _yfordeck;
        _headlineitem.width = layout.Article.headlinewidth;
        _headlineitem.height = layout.Article.headlinelength;
        _headlineitem.type = "headline";
        _headlineitem.size = layout.Article.headlinecaption;
        _yfordeck += _headlineitem.height;
        _pageitems.Add(_headlineitem);

        if (layout.LayoutA != null && layout.LayoutA.PlacedImages != null)
        {
            layout.LayoutA.PlacedImages.ForEach(img =>
            {
                //_pageitems.Add(Helper.GetImageItem(img.Item1, article.x + img.Item2, img.Item3 + _yfordeck + layout.TextBlock.height, article.article_id)));

                var pictureStoryItem = Helper.GetImageItem(img.Item1, img.Item2, img.Item3, article.article_id);
                if (pictureStoryItem.caption != null && pictureStoryItem.caption.height <= 0)
                {
                    pictureStoryItem.caption.width = -1;
                    pictureStoryItem.caption.height = -1;
                    pictureStoryItem.caption.x = -1;
                    pictureStoryItem.caption.y = -1;
                }
                _pageitems.Add(pictureStoryItem);
            });
        }

        if (layout.LayoutB != null && layout.LayoutB.PlacedImages != null)
        {
            layout.LayoutB.PlacedImages.ForEach(img =>
            {
                //_pageitems.Add(Helper.GetImageItem(img.Item1, article.x + img.Item2, img.Item3 + _yfordeck + layout.TextBlock.height, article.article_id)));

                var pictureStoryItem = Helper.GetImageItem(img.Item1, img.Item2, img.Item3, article.article_id);
                if (pictureStoryItem.caption != null && pictureStoryItem.caption.height <= 0)
                {
                    pictureStoryItem.caption.width = -1;
                    pictureStoryItem.caption.height = -1;
                    pictureStoryItem.caption.x = -1;
                    pictureStoryItem.caption.y = -1;
                }
                _pageitems.Add(pictureStoryItem);
            });
        }

        if (layout.Article.preamble > 0)
        {
            AutomationPageArticleDeck _deck = new AutomationPageArticleDeck() { x = layout.TextBlock.x, width = 1 };
            _deck.y = _yfordeck;
            _deck.height = layout.Article.preamble;
            _deck.type = "deck";
            _pageitems.Add(_deck);
        }
        //Adding byline

        if (layout.Article.byline > 0)
        {
            AutomationPageArticleByline _byline = new AutomationPageArticleByline() { x = layout.TextBlock.x, width = 1 };
            _byline.y = _yfordeck + layout.Article.preamble;
            _byline.height = layout.Article.byline;
            _byline.type = "byline";
            _pageitems.Add(_byline);
        }
        article.items = _pageitems;
        return article;
    }


    private AutomationPageArticle GenerateJumpArticleJson(Box _article, string articletype = "")
    {
        AutomationPageArticle article = new AutomationPageArticle();
        article.article_id = _article.Id;
        article.x = (int)_article.position.pos_x;
        article.y = (int)_article.position.pos_z;
        article.height = (int)_article.length;
        article.width = (int)_article.width;
        article.slug = "";
        article.headline = headlineMap.ContainsKey(_article.Id) ? headlineMap[_article.Id].ToString() : "";

        ArrayList _pageitems = new ArrayList();

        //add headline
        if (_article.headlinePosition != null)
        {
            AutomationPageArticleHeadline _headlineitem = new AutomationPageArticleHeadline();
            PlaceJumpArticleItem(_headlineitem, _article.headlinePosition);
            _headlineitem.type = "headline";
            _headlineitem.size = _article.headlinecaption;
            _pageitems.Add(_headlineitem);
        }

        if (_article.kickerPosition != null)
        {
            AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
            PlaceJumpArticleItem(_kickeritem, _article.kickerPosition);
            _kickeritem.type = "kicker";
            _kickeritem.size = null;
            _pageitems.Add(_kickeritem);
        }

        if (_article.preamblePosition != null)
        {
            AutomationPageArticleDeck _deck = new AutomationPageArticleDeck();
            PlaceJumpArticleItem(_deck, _article.preamblePosition);
            _deck.type = "deck";
            _pageitems.Add(_deck);
        }
        //Adding byline

        if (_article.bylinePosition != null)
        {
            AutomationPageArticleByline _byline = new AutomationPageArticleByline();
            PlaceJumpArticleItem(_byline, _article.bylinePosition);
            _byline.type = "byline";
            _pageitems.Add(_byline);
        }


        if (_article.usedImageList != null && _article.usedImageList.Count > 0)
        {
            foreach (var image in _article.usedImageList)
            {
                if (articletype == "adwrap")
                    _pageitems.Add(Helper.GetImageItem(image, image.position.pos_x, image.position.pos_z, _article.Id));
                else
                    _pageitems.Add(Helper.GetJumpImageItem(image, _article.Id));
            }
        }
        if (_article.jumpTolength > 0)
        {
            AutomationPageArticleByJump _jump = new AutomationPageArticleByJump();
            _jump.x = article.x + article.width - _article.jumpTowidth;
            _jump.y = article.y + article.height - _article.jumpTolength;
            _jump.width = _article.jumpTowidth;
            _jump.height = _article.jumpTolength;
            _jump.type = "jumpTo";
            _jump.fromToPageId = _article.jumptopageid;
            _pageitems.Add(_jump);
        }
        article.items = _pageitems;
        return article;
    }

    private void GenerateErrorJsonFile(string sErrorFile, string errMessage)
    {
        ErrorOutput errOut = new ErrorOutput();
        errOut.error = true;
        errOut.message = DateTime.Now.ToString() + " " + errMessage;

        JsonSerializerOptions o = new JsonSerializerOptions();
        o.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

        System.IO.FileStream stream = System.IO.File.Create(sErrorFile);
        System.Text.Json.JsonSerializer.Serialize(stream, errOut, o);
        stream.Flush();
        stream.Close();
    }
    public List<Box> FindAllArticleSizesForMultiFacts(Box _box, Image _mainImage, Image _subImage, List<Image> _fctList, Image _citImage)
    {

        Image _tmainimg = null;
        Image _tsubimg1 = null;
        //Image _tsubimg2 = null;
        //Image _tfct1 = null;
        //Image _tfct2 = null;
        //Image _tfct3 = null;
        Image _tcitimage = null;
        List<Image> _tfctList = null;

        List<Box> _sizes = new List<Box>();

        double _area;
        int _ipreamble = _box.preamble > 0 ? 1 : 0;
        int _totfctwidth = 0, _iareafact = 0, _maxlenfact = 0;
        foreach (Image _tf in _fctList)
        {
            _totfctwidth += _tf.width;
            _iareafact += _tf.length * _tf.width;
            if (_tf.length > _maxlenfact)
                _maxlenfact = _tf.length;
        }

        if (_totfctwidth > _mainImage.width)
            return _sizes;

        foreach (int _width in _box.avalableLengths())
        {

            //if (_subImgList.Count() == 2)
            //    _tsubimg2 = Helper.DeepCloneImage(_subImgList[1]);

            //_tfct1 = Helper.DeepCloneImage(_fctList[0]);
            //_tfct2 = Helper.DeepCloneImage(_fctList[1]);

            //if (_fctList.Count() > 2)
            //    _tfct3 = Helper.DeepCloneImage(_fctList[2]);

            double _iarea1 = 0, _citarea = 0, _newboxarea;
            _tmainimg = Helper.CustomCloneImage(_mainImage);
            _tfctList = Helper.CustomCloneListImage(_fctList);
            if (_citImage != null)
            {
                _tcitimage = Helper.CustomCloneImage(_citImage);
                _citarea = _tcitimage.width * _tcitimage.length;
            }
            _area = _box.origArea;
            _iarea1 = _tmainimg.length * _tmainimg.width;


            int _maxsublength = 0;// GetMaxlengthOfSubImages(_subimages);
            bool invalidfactsize = _fctList.Any(x => !Helper.isValidFact(x));

            //Width of the final article cannot be less than the image width
            if (_width < _tmainimg.width)
                continue;

            if (invalidfactsize)
                continue;

            if (_totfctwidth > _tmainimg.width || _totfctwidth >= _width)
                continue;

            _newboxarea = _area + _iarea1 + _iareafact;
            int _length = (int)Math.Ceiling(_newboxarea / _width);

            bool cantproceed = true;
            if (_tmainimg.width == _width)
            {
                if (_maxlenfact <= _length - _tmainimg.length)
                {
                    cantproceed = false;
                    _tmainimg.aboveHeadline = true;
                    Helper.MoveLargestFactsToRight(_tfctList, _length - _tmainimg.length);
                    int _x = _width - _totfctwidth;
                    foreach (Image _tf in _tfctList)
                    {
                        _tf.topimageinsidearticle = true;
                        _tf.relativey = 0;
                        _tf.relativex = _x;
                        _x += _tf.width;
                    }
                }
                else
                    continue;

            }
            else //Main image width < Article width: We need to fit Main image and side images/citation above the headline
            {
                if (_maxlenfact <= _length - _tmainimg.length)
                {
                    cantproceed = false;
                    _tmainimg.aboveHeadline = false;
                    _tmainimg.topimageinsidearticle = true;
                    _tmainimg.relativey = 0;
                    _tmainimg.relativex = _width - _tmainimg.width;
                    Helper.MoveLargestFactsToRight(_tfctList, _length - _tmainimg.length);

                    int _x = _width - _totfctwidth;
                    foreach (Image _tf in _tfctList)
                    {
                        _tf.relativey = _tmainimg.length;
                        _tf.relativex = _x;
                        _x += _tf.width;
                    }
                }
                else
                    continue;
            }

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.multiFactStackingOrder = "Horizontal";
            _newbox.usedImageList = new List<Image>();
            _newbox.usedImageList.Add(_tmainimg);
            foreach (Image _timage in _tfctList)
                _newbox.usedImageList.Add(_timage);

            _sizes.Add(_newbox);
        }

        return _sizes;
    }
    public List<Box> FindAllArticleSizesForMultiFactNoImage(Box _box, List<Image> _fctList)
    {
        List<Box> _sizes = new List<Box>();
        _sizes = FindAllArticleSizesForMultiFactNoImage(_box, _fctList, 0);
        //Try to find the fit by adding whitepaces
        int _totalsizes = _box.avalableLengths().Count();
        int _sizesinlayouts = _sizes.Count();
        int _startwhilelines = ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        while (ModelSettings.clsWhiteSpaceSettings.addwhitespacelines && _sizesinlayouts < _totalsizes
            && _startwhilelines <= ModelSettings.clsWhiteSpaceSettings.maxwhitespacelines)
        {
            foreach (var _vsize in _box.avalableLengths())
            {
                if (_sizes.Where(x => x.width == _vsize).Count() == 0)
                {
                    Box _newbox = Helper.CustomCloneBox(_box);
                    _newbox.origArea += _startwhilelines;
                    _newbox.whitespace = _startwhilelines;
                    List<Box> _whitespaceboxes = FindAllArticleSizesForMultiFactNoImage(_newbox, _fctList, _vsize);
                    if (_whitespaceboxes != null && _whitespaceboxes.Count > 0)
                        _sizes.AddRange(_whitespaceboxes);
                }
            }
            _sizesinlayouts = _sizes.Count();
            _startwhilelines += ModelSettings.clsWhiteSpaceSettings.minwhitespacelines;
        }
        return _sizes;
    }
    public List<Box> FindAllArticleSizesForMultiFactNoImage(Box _box, List<Image> _fctList, int _boxwidth)
    {

        List<Image> _tfctList = null;
        List<Box> _sizes = new List<Box>();

        double _area;
        int _ipreamble = _box.preamble > 0 ? 1 : 0;
        int _totfctwidth = 0, _iareafact = 0, _maxlenfact = 0;
        foreach (Image _tf in _fctList)
        {
            _totfctwidth += _tf.width;
            _iareafact += _tf.length * _tf.width;
            if (_tf.length > _maxlenfact)
                _maxlenfact = _tf.length;
        }

        foreach (int _width in _box.avalableLengths())
        {
            if (_boxwidth > 0 && _width != _boxwidth)
                continue;

            double _newboxarea;

            _tfctList = Helper.CustomCloneListImage(_fctList);
            _area = _box.origArea;
            if (_totfctwidth >= _width)
                continue;

            _newboxarea = _area + _iareafact;
            int _length = (int)Math.Ceiling(_newboxarea / _width);
            bool invalidfactsize = _fctList.Any(x => !Helper.isValidFact(x));

            if (!invalidfactsize && _maxlenfact <= _length)
            {
                Helper.MoveLargestFactsToRight(_tfctList, _length);
                int _x = _width - _totfctwidth;
                foreach (Image _tf in _tfctList)
                {
                    _tf.topimageinsidearticle = true;
                    _tf.relativey = 0;
                    _tf.relativex = _x;
                    _x += _tf.width;
                }
            }
            else
                continue;

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.multiFactStackingOrder = "Horizontal";
            _newbox.usedImageList = new List<Image>();
            foreach (Image _timage in _tfctList)
                _newbox.usedImageList.Add(_timage);

            _sizes.Add(_newbox);
        }

        return _sizes;
    }

    public List<Box> FindAllArticleSizesForTwoFact(Box _box, Image _mainImage, Image _subImgage, List<Image> _fctList, Image _citImage)
    {

        Image _tmainimg = null;
        Image _tsubimg1 = null;
        Image _tcitimage = null;
        List<Image> _tfctList = null;
        List<Box> _sizes = new List<Box>();

        double _area;
        int _ipreamble = _box.preamble > 0 ? 1 : 0;

        foreach (int _width in _box.avalableLengths())
        {
            _tfctList = Helper.CustomCloneListImage(_fctList);

            _tmainimg = Helper.CustomCloneImage(_mainImage);
            if (_citImage != null)
                _tcitimage = Helper.CustomCloneImage(_citImage);

            if (_subImgage != null)
                _tsubimg1 = Helper.CustomCloneImage(_subImgage);

            double _iarea1 = 0, _iareasubimage = 0, _iareaquote = 0, _newboxarea = 0, _iareafact = 0;
            int _totfctwidth = 0, _maxlenfact = 0;

            _area = _box.origArea;
            _iarea1 = _tmainimg.length * _tmainimg.width;
            foreach (Image _tf in _tfctList)
            {
                _iareafact += _tf.length * _tf.width;
                _totfctwidth += _tf.width;
                if (_tf.length > _maxlenfact)
                    _maxlenfact = _tf.length;
            }

            if (_tsubimg1 != null)
                _iareasubimage = _tsubimg1.length * _tsubimg1.width;
            if (_tcitimage != null)
                _iareaquote = _tcitimage.length * _tcitimage.width;

            _newboxarea = _area + _iarea1 + _iareasubimage + _iareaquote + _iareafact;


            int _maxsublength = GetMaxlengthOfSubImages(_tfctList);
            bool invalidfactsize = _fctList.Any(x => !Helper.isValidFact(x));

            //Width of the final article cannot be less than the image width
            if (_width < _tmainimg.width)
                continue;

            if (invalidfactsize)
                continue;

            if (_totfctwidth > _tmainimg.width || _totfctwidth >= _width)
                continue;

            int _length = (int)Math.Ceiling(_newboxarea / _width);

            bool cantproceed = true;
            if (_tmainimg.width == _width)
            {
                _tmainimg.aboveHeadline = true;
                int _remaininglength = _length - _tmainimg.length;

                //2 possibilities: Fact and Image/citation can be placed horizonatally or Vertically
                //Horizontally
                if (_tsubimg1 != null)
                {
                    if (_tsubimg1.width + _totfctwidth < _width)
                    {
                        if (_remaininglength >= _maxsublength && _remaininglength >= _tsubimg1.length)
                        {
                            _tsubimg1.topimageinsidearticle = true;
                            cantproceed = false;
                            Helper.MoveLargestFactsToRight(_tfctList, _remaininglength);
                            int _x = _width - (_tsubimg1.width + _totfctwidth);
                            if (_maxsublength > _tsubimg1.length)
                            {
                                _tsubimg1.relativex = _x;
                                _x += _tsubimg1.width;
                                foreach (var _ti in _tfctList)
                                {
                                    _ti.relativex = _x;
                                    _x += _ti.width;
                                }
                            }
                            else
                            {
                                foreach (var _ti in _tfctList)
                                {
                                    _ti.relativex = _x;
                                    _x += _ti.width;
                                }
                                _tsubimg1.relativex = _x;

                            }
                        }
                    }

                }
                else if (_tcitimage != null)
                {
                    if (_tcitimage.width + _totfctwidth < _box.width)
                    {
                        if (_remaininglength >= _maxsublength && _remaininglength >= _tcitimage.length)
                        {
                            _tcitimage.topimageinsidearticle = true;
                            cantproceed = false;
                            Helper.MoveLargestFactsToRight(_tfctList, _remaininglength);
                            int _x = _width - (_tcitimage.width + _totfctwidth);

                            _tcitimage.relativex = _x;
                            _x += _tcitimage.width;
                            foreach (var _ti in _tfctList)
                            {
                                _ti.relativex = _x;
                                _x += _ti.width;
                            }
                        }
                    }
                }
                else //both sub image and citations are null
                {
                    if (_maxsublength <= _remaininglength)
                    {
                        cantproceed = false;
                        int _x = _width - _totfctwidth;
                        foreach (Image _tf in _tfctList)
                        {
                            _tf.relativex = _x;
                            _x += _tf.width;
                        }
                    }
                }

                if (cantproceed)
                    continue;
            }
            else //Main image width < Article width: We need to fit Main image and side images/citation above the headline
            {
                //Ignore subimage and citation
                _tsubimg1 = null;
                _tcitimage = null;
                _newboxarea = _area + _iarea1 + _iareafact;
                _length = (int)Math.Ceiling(_newboxarea / _width);
                int _remaininglength = _length - _mainImage.length;

                if (_maxlenfact <= _length - _tmainimg.length)
                {
                    cantproceed = false;
                    _tmainimg.aboveHeadline = false;
                    _tmainimg.topimageinsidearticle = true;
                    _tmainimg.relativey = 0;
                    _tmainimg.relativex = _width - _tmainimg.width;
                    Helper.MoveLargestFactsToRight(_tfctList, _length - _tmainimg.length);

                    int _x = _width - _totfctwidth;
                    foreach (Image _tf in _tfctList)
                    {
                        _tf.relativey = _tmainimg.length;
                        _tf.relativex = _x;
                        _x += _tf.width;
                    }
                }
                else
                    continue;
            }

            Box _newbox = Helper.CustomCloneBox(_box);

            _newbox.length = _length;
            _newbox.width = _width;
            _newbox.volume = _length * _width;
            _newbox.multiFactStackingOrder = "Horizontal";
            _newbox.usedImageList = new List<Image>();
            _newbox.usedImageList.Add(_tmainimg);
            foreach (Image _timage in _tfctList)
                _newbox.usedImageList.Add(_timage);
            if (_tsubimg1 != null)
                _newbox.usedImageList.Add(_tsubimg1);
            if (_tcitimage != null)
                _newbox.usedImageList.Add(_tcitimage);
            _sizes.Add(_newbox);
        }

        return _sizes;
    }

    private void BuildDoubleTruckPages(List<Box> filteredarticles, List<PageInfo> lstFilteredPages, int mandatoryListOrderSection)
    {
        List<Box> dtArticles = filteredarticles.Where(x => x.isdoubletruck == true && x.articletype.ToLower() != "picturestories").OrderBy(x => x.rank).ToList();
        if (ModelSettings.hasdoubletruck == 1 && dtArticles.Count() > 0)
        {
            if (mandatoryListOrderSection == 1)
            {
                List<Box> aarticleList = null;

                if (ModelSettings.newPageEnabled)
                    aarticleList = filteredarticles.Where(x => x.isNewPage == true).OrderBy(x => x.rank).ToList();
                else
                    aarticleList = filteredarticles.Where(x => x.priority == 5).OrderBy(x => x.rank).ToList();

                for (int _icnt = 0; _icnt < aarticleList.Count(); _icnt++)
                {

                    if (!aarticleList[_icnt].isdoubletruck || aarticleList[_icnt].articletype.ToLower() == "picturestories")
                        continue;

                    if (aarticleList[_icnt].spreadPageCount > 0)
                        continue;

                    Box _dtarticle = aarticleList[_icnt];
                    if (_icnt + 1 >= lstFilteredPages.Count())
                    {

                        filteredarticles.RemoveAll(x => x.Id == _dtarticle.Id);
                        filteredarticles.RemoveAll(x => x.parentArticleId == _dtarticle.Id);
                        aarticleList.RemoveAll(x => x.Id == _dtarticle.Id);

                        //NT - added the if condition only to evade the exception that gets triggered
                        //if multispread consumes all pages (insufficient pages for a given section)
                        if (lstFilteredPages.Count > 0 && lstFilteredPages.Count > _icnt)
                            lstFilteredPages.RemoveAt(_icnt);

                        break;
                    }

                    PageInfo firstPage = lstFilteredPages[_icnt];
                    PageInfo secondPage = lstFilteredPages[_icnt + 1];

                    bool bcanprint = true;
                    //if its not the even page then continue;
                    if (firstPage.pageid % 2 != 0)
                        bcanprint = false;
                    //if both the pages are not consecutive pages then continue
                    if (secondPage.pageid != firstPage.pageid + 1)
                        bcanprint = false;
                    //if any of the page has ads then continue;
                    //if (firstPage.ads.Count() > 0 || secondPage.ads.Count() > 0)
                    //    bcanprint = false;

                    if (bcanprint)
                    {
                        Log.Information("Building double truck page for article: {articleId}", _dtarticle.Id);
                        bool articlefitted = BuildDoubleTruckPage(_dtarticle, firstPage, secondPage);
                        if (articlefitted)
                        {
                            firstPage.doubletruckpage = true;
                            secondPage.doubletruckpage = true;
                        }
                        Log.Information("completed double truck page for article: {articleId}", _dtarticle.Id);
                    }
                    else
                        Log.Information("completed Can't print the DT article: {articleId}, on page: {pageId}", _dtarticle.Id, firstPage.pageid);

                    filteredarticles.RemoveAll(x => x.Id == _dtarticle.Id);
                    filteredarticles.RemoveAll(x => x.parentArticleId == _dtarticle.Id);
                    aarticleList.RemoveAll(x => x.Id == _dtarticle.Id);
                    lstFilteredPages.Remove(firstPage);
                    lstFilteredPages.Remove(secondPage);
                    _icnt--;
                }
            }

            if (mandatoryListOrderSection == 0)
            {
                foreach (Box _dtarticle in dtArticles)
                {
                    if (!_dtarticle.isdoubletruck || _dtarticle.articletype.ToLower() == "picturestories")
                        continue;

                    if (_dtarticle.spreadPageCount > 0)
                        continue;

                    int _i = 0;
                    while (_i < lstFilteredPages.Count() - 1)
                    {
                        PageInfo firstPage = lstFilteredPages[_i];
                        PageInfo secondPage = lstFilteredPages[_i + 1];

                        _i++;
                        //if its not the even page then continue;
                        if (firstPage.pageid % 2 != 0)
                            continue;
                        //if both the pages are not consecutive pages then continue
                        if (secondPage.pageid != firstPage.pageid + 1)
                            continue;
                        //if any of the page has ads then continue;
                        if (firstPage.ads.Count() > 0 || secondPage.ads.Count() > 0)
                            continue;

                        Log.Information("Building double truck page for article: {articleId}", _dtarticle.Id);
                        bool articlefitted = BuildDoubleTruckPage(_dtarticle, firstPage, secondPage);
                        if (articlefitted)
                        {
                            firstPage.doubletruckpage = true;
                            secondPage.doubletruckpage = true;
                            filteredarticles.Remove(_dtarticle);
                            filteredarticles.RemoveAll(x => x.parentArticleId == _dtarticle.Id);
                            lstFilteredPages.Remove(firstPage);
                            lstFilteredPages.Remove(secondPage);
                        }
                        Log.Information("completed double truck page for article: {articleId}", _dtarticle.Id);
                        break;

                    }
                }
            }
        }
    }


    private void BuildPictureStoriesSpread(List<Box> listOfArticles, List<PageInfo> lstFilteredPages, int mandatoryListOrderSection, String _section)
    {
        var picStoryDTArticles = listOfArticles.Where(x => x.isdoubletruck == true && x.articletype.ToLower() == "picturestories").OrderBy(x => x.rank).ToList();
        if (ModelSettings.picturestoriesdtenabled && picStoryDTArticles.Count > 0)
        {
            if (mandatoryListOrderSection == 1)
            {
                List<Box> aarticleList = null;

                if (ModelSettings.newPageEnabled)
                    aarticleList = listOfArticles.Where(x => x.isNewPage == true).OrderBy(x => x.rank).ToList();
                else
                    aarticleList = listOfArticles.Where(x => x.priority == 5).OrderBy(x => x.rank).ToList();

                var skipPages = 0;
                for (int _icnt = 0; _icnt < aarticleList.Count(); _icnt++)
                {
                    if (_icnt + 1 >= lstFilteredPages.Count())
                        break;

                    if (!aarticleList[_icnt].isdoubletruck)
                    {
                        skipPages++;
                        continue;
                    }

                    if (aarticleList[_icnt].articletype != "picturestories")
                    {
                        skipPages += 2;
                        continue;
                    }

                    Box _dtarticle = aarticleList[_icnt];
                    var selectedPages = lstFilteredPages.Skip(skipPages).Take(2).ToList();

                    PageInfo firstPage = selectedPages[0];
                    PageInfo secondPage = selectedPages[1];

                    bool bcanprint = true;
                    //if its not the even page then continue;
                    if (firstPage.pageid % 2 != 0)
                        bcanprint = false;
                    //if both the pages are not consecutive pages then continue
                    if (secondPage.pageid != firstPage.pageid + 1)
                        bcanprint = false;

                    if (bcanprint)
                    {
                        bool articlefitted;
                        if (ModelSettings.enableNewAlgorithmForPictureStory)
                        {
                            PictureStory psObject = new PictureStory(true, firstPage, kickersmap, headlines, _dtarticle);
                            psObject.InitDoubleTruck(firstPage, secondPage);
                            articlefitted = psObject.Generate();

                        }
                        else
                        {
                            var picStoryDTLayout = new PictureStoriesDT(_dtarticle, firstPage, secondPage, headlines, kickersmap);
                            articlefitted = picStoryDTLayout.BuildDoublePage();
                        }
                        if (articlefitted)
                        {
                            firstPage.doubletruckpage = true;
                            secondPage.doubletruckpage = true;
                        }
                    }

                    listOfArticles.RemoveAll(x => x.Id == _dtarticle.Id);
                    listOfArticles.RemoveAll(x => x.parentArticleId == _dtarticle.Id);
                    aarticleList.RemoveAll(x => x.Id == _dtarticle.Id);
                    selectedPages.ForEach(page => lstFilteredPages.Remove(page));
                    _icnt--;
                }
            }
            if (mandatoryListOrderSection == 0)
            {
                foreach (Box _dtarticle in picStoryDTArticles)
                {
                    if (!_dtarticle.isdoubletruck || _dtarticle.articletype.ToLower() != "picturestories")
                        continue;

                    if (_dtarticle.spreadPageCount > 0)
                        continue;

                    int _i = 0;
                    while (_i < lstFilteredPages.Count() - 1)
                    {
                        PageInfo firstPage = lstFilteredPages[_i];
                        PageInfo secondPage = lstFilteredPages[_i + 1];

                        _i++;
                        //if its not the even page then continue;
                        if (firstPage.pageid % 2 != 0)
                            continue;
                        //if both the pages are not consecutive pages then continue
                        if (secondPage.pageid != firstPage.pageid + 1)
                            continue;
                        //if any of the page has ads then continue;
                        if (firstPage.ads.Count() > 0 || secondPage.ads.Count() > 0)
                            continue;

                        bool articlefitted;
                        if (ModelSettings.enableNewAlgorithmForPictureStory)
                        {
                            PictureStory psObject = new PictureStory(true, firstPage, kickersmap, headlines, _dtarticle);
                            psObject.InitDoubleTruck(firstPage, secondPage);
                            articlefitted = psObject.Generate();
                        }
                        else
                        {
                            var picStoryDTLayout = new PictureStoriesDT(_dtarticle, firstPage, secondPage, headlines, kickersmap);
                            articlefitted = picStoryDTLayout.BuildDoublePage();
                        }

                        if (articlefitted)
                        {
                            firstPage.doubletruckpage = true;
                            secondPage.doubletruckpage = true;
                            listOfArticles.Remove(_dtarticle);
                            lstFilteredPages.Remove(firstPage);
                            lstFilteredPages.Remove(secondPage);
                            Log.Information("Removed article and pages for article: {articleId}", _dtarticle.Id);
                        }

                        break;

                    }
                }
            }
        }
    }
    private void BuildPictureStories(List<Box> listOfArticles, List<PageInfo> listOfPages, int mandatoryListOrderSection)
    {
        if (!ModelSettings.picturestoriesenabled || listOfArticles.Count(x => x.articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase) && !x.isdoubletruck) <= 0)
            return;
        if (mandatoryListOrderSection == 1)
        {
            List<Box> A_PriorityArticles = null;
            if (ModelSettings.newPageEnabled)
            {
                A_PriorityArticles = listOfArticles.Where(x => x.isNewPage == true).OrderBy(x => x.rank).ToList();
            }
            else
            {
                A_PriorityArticles = listOfArticles.Where(x => x.priority == 5).OrderBy(x => x.rank).ToList();
            }

            for (int _icnt = 0; _icnt < A_PriorityArticles.Count(); _icnt++)
            {
                if (_icnt >= listOfPages.Count())
                {
                    break;
                }
                if (!A_PriorityArticles[_icnt].articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase) || A_PriorityArticles[_icnt].isdoubletruck)
                {
                    continue;
                }
                Box pictueStoryArticle = A_PriorityArticles[_icnt];
                PageInfo firstPage = listOfPages[_icnt];

                if (ModelSettings.enableNewAlgorithmForPictureStory)
                {
                    PictureStory psObject = new PictureStory(false, firstPage, kickersmap, headlines, pictueStoryArticle);
                    psObject.Generate();
                }
                else
                {
                    SinglePagePictureStory picstories = new SinglePagePictureStory(pictueStoryArticle, firstPage, kickersmap, headlines);
                    picstories.GeneratePictureStoriesLayout();
                }

                int psArticleIdx = listOfArticles.FindIndex(x => x.Id == pictueStoryArticle.Id);
                if (psArticleIdx != -1)
                {
                    for (int idx = psArticleIdx + 1; idx < listOfArticles.Count;)
                    {
                        if (listOfArticles[idx].priority == 5 || listOfArticles[idx].isNewPage)
                            break;
                        listOfArticles.RemoveAt(idx);
                    }
                }

                listOfArticles.RemoveAll(x => x.Id == pictueStoryArticle.Id);
                A_PriorityArticles.RemoveAll(x => x.Id == pictueStoryArticle.Id);
                listOfPages.Remove(firstPage);

                _icnt--;
            }
        }
        if (mandatoryListOrderSection == 0)
        {
            foreach (Box _picarticle in listOfArticles.Where(x => x.articletype.Equals("picturestories", StringComparison.OrdinalIgnoreCase) && !x.isdoubletruck).OrderBy(x => x.rank).ToList())
            {
                int _i = 0;
                while (_i < listOfPages.Count() - 1)
                {
                    PageInfo firstPage = listOfPages[_i];
                    _i++;

                    //if any of the page has ads then continue;
                    if (firstPage.ads.Count() > 0)
                        continue;
                    bool retvalue;
                    Log.Information("Building picture story for article: {articleId}", _picarticle.Id);
                    if (ModelSettings.enableNewAlgorithmForPictureStory)
                    {
                        PictureStory psObject = new PictureStory(false, firstPage, kickersmap, headlines, _picarticle);
                        retvalue = psObject.Generate();
                    }
                    else
                    {
                        SinglePagePictureStory picstories = new SinglePagePictureStory(_picarticle, firstPage, kickersmap, headlines);
                        retvalue = picstories.GeneratePictureStoriesLayout();
                    }
                    if (retvalue)
                        listOfPages.Remove(firstPage);
                    else
                        Log.Information("Can't print the pic story article: {id}, on page: {pageId}", _picarticle.Id, firstPage.pageid);

                    listOfArticles.RemoveAll(x => x.Id == _picarticle.Id);

                    break;

                }
            }
        }
    }

    private void SetPlacementRules()
    {
        if (!ModelSettings.bCustomPlacementEnabled)
            return;

        foreach (var _info in lstPages)
        {
            if (_info.pageid % 2 == 0)
                _info.placementrule = ModelSettings.placementRules.defaultevenpage;
            else
                _info.placementrule = ModelSettings.placementRules.defaultoddpage;
        }
        foreach (var _sectionplacement in ModelSettings.placementRules.oddpagerule)
        {
            foreach (var item in lstPages.Where(x => x.section.ToLower() == _sectionplacement.section.ToLower() && x.pageid % 2 > 0))
            {
                item.placementrule = _sectionplacement.rule;
            }
        }
        foreach (var _sectionplacement in ModelSettings.placementRules.evenpagerule)
        {
            foreach (var item in lstPages.Where(x => x.section.ToLower() == _sectionplacement.section.ToLower() && x.pageid % 2 == 0))
            {
                item.placementrule = _sectionplacement.rule;
            }
        }


    }
    private void GetArticleCombinationforMultiFact(Box _box, List<Box> _boxlist, List<Image> _mainimage, List<Image> _subimage, List<List<Image>> _fctImageList, List<Image> _tempcitationList)
    {

        foreach (List<Image> _fctlist in _fctImageList)
        {
            List<Box> _tempboxlist = FindAllArticleSizesForMultiFactNoImage(_box, _fctlist);
            if (_tempboxlist.Count > 0)
                _boxlist.AddRange(_tempboxlist);
        }

        if (_mainimage != null)
        {
            //_tempcitationList = null;
            if (_fctImageList.Count() == 2)
            {
                foreach (Image _main in _mainimage)
                {
                    foreach (List<Image> _fctlist in _fctImageList)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizesForTwoFact(_box, _main, null, _fctlist, null);
                        if (_tempboxlist.Count > 0)
                            _boxlist.AddRange(_tempboxlist);
                    }
                }

                if (_tempcitationList != null)
                {
                    foreach (Image _main in _mainimage)
                    {
                        foreach (List<Image> _fctlist in _fctImageList)
                            foreach (Image _citimg in _tempcitationList)
                            {
                                List<Box> _tempboxlist = FindAllArticleSizesForTwoFact(_box, _main, null, _fctlist, _citimg);
                                if (_tempboxlist.Count > 0)
                                    _boxlist.AddRange(_tempboxlist);
                            }
                    }
                }

                if (_subimage != null)
                {
                    foreach (Image _main in _mainimage)
                    {
                        foreach (Image _sub in _subimage)
                            foreach (List<Image> _fctlist in _fctImageList)
                            {
                                List<Box> _tempboxlist = FindAllArticleSizesForTwoFact(_box, _main, _sub, _fctlist, null);
                                if (_tempboxlist.Count > 0)
                                    _boxlist.AddRange(_tempboxlist);
                            }
                    }
                }

            }
            else
            {
                foreach (Image _main in _mainimage)
                {
                    foreach (List<Image> _fctlist in _fctImageList)
                    {
                        List<Box> _tempboxlist = FindAllArticleSizesForMultiFacts(_box, _main, null, _fctlist, null);
                        if (_tempboxlist.Count > 0)
                            _boxlist.AddRange(_tempboxlist);
                    }
                }
            }
        }

    }



    private bool CheckPlacementofLowerPriorityArticles(List<Box> _boxes, int _startingx, int _width, bool _adsonleft)
    {
        if (_boxes == null)
            return false;
        //if (_boxes[0].width == 4 && _boxes.Count==5 && _boxes[3].position !=null)
        //    _adsonleft = _adsonleft;

        if (!_adsonleft && _boxes.Exists(x => x.priority <= 3 && x.position != null && x.position.pos_x + x.width != _startingx + _width))
            return false;
        else if (_adsonleft && _boxes.Exists(x => x.priority <= 3 && x.position != null && x.position.pos_x - x.width != _startingx))
            return false;
        else
            return true;
    }

    private ArticlePosition LoadJumpArticlePositions(JsonElement element)
    {

        ArticlePosition _articleposition = null;

        if (element.TryGetProperty("articleposition", out var articleelement))
        {
            foreach (var t in articleelement.EnumerateArray())
            {
                _articleposition = t.Deserialize<ArticlePosition>();
                break;
            }
        }

        return _articleposition;
    }

    private Jumps LoadJumpFromToData(JsonElement jumpElement)
    {
        Jumps jumps = new Jumps();
        foreach (var element in jumpElement.EnumerateArray())
        {
            if (element.GetProperty("name").GetString().Equals("jumpfrom", StringComparison.OrdinalIgnoreCase))
            {
                List<JumpLine> lstjumpfrom = LoadJumpElementData(element.GetProperty("columns"));
                jumps.lstJumpFrom = lstjumpfrom;
            }
            if (element.GetProperty("name").GetString().Equals("jumpto", StringComparison.OrdinalIgnoreCase))
            {
                List<JumpLine> lstjumpto = LoadJumpElementData(element.GetProperty("columns"));
                jumps.lstJumpTo = lstjumpto;
            }
            if (element.GetProperty("name").GetString().Equals("jumpHeadline", StringComparison.OrdinalIgnoreCase))
            {
                List<JumpLine> lstjumpheadline = LoadJumpElementData(element.GetProperty("columns"));
                jumps.lstJumpHeadline = lstjumpheadline;
            }
        }

        return jumps;
    }

    private List<JumpLine> LoadJumpElementData(JsonElement jumplines)
    {
        List<JumpLine> lstlines = new List<JumpLine>();
        foreach (var element in jumplines.EnumerateArray())
        {
            int _column = int.Parse(element.GetProperty("nCols").GetString());
            int _lines = int.Parse(element.GetProperty("lines").GetString());
            int _typolines = int.Parse(element.GetProperty("typoLines").GetString());
            JumpLine line = new JumpLine() { column = _column, lines = _lines, typolines = _typolines };
            lstlines.Add(line);
        }

        return lstlines;
    }
    private ScoreList GenerateJumpFrontPage(List<Box> jumplist)
    {
        ScoreList _sc = null;
        List<Box> _articlelist = new List<Box>();
        foreach (var jumparticle in jumplist.Where(x => x.isjumparticle))
        {
            int _totalarticlearea = 0;
            int _imagearea = 0;
            int _hlarea = 0;
            int _kickerarea = 0;
            int _bylinearea = 0;
            int _preamblearea = 0;
            ArticlePosition _position = dictArticlePositions.lstArticlePositions[jumparticle.Id];

            Box _newbox = Helper.DeepCloneBox(jumparticle);
            _newbox.width = double.Parse(_position.layout.width);
            _newbox.length = double.Parse(_position.layout.height);
            Node n = new Node() { pos_x = int.Parse(_position.layout.x), pos_z = int.Parse(_position.layout.y), width = int.Parse(_position.layout.width), length = int.Parse(_position.layout.height), isOccupied = true };
            _newbox.position = n;
            _newbox.pos_x = int.Parse(_position.layout.x);
            _newbox.pos_z = int.Parse(_position.layout.y);
            //_newbox.headlinecaption = "small";
            _newbox.usedImageList = new List<Image>();
            _newbox.articletype = "jumps";

            using (ArticleItem _item = _position.layout.GetHeadlineItem())
            {
                Node _n = GetPositionNode(_item);
                _newbox.headlinePosition = _n;
                _newbox.headlinewidth = _n != null ? _n.width : 0;
                _newbox.headlinelength = (_n != null ? _n.length : 0);
                _newbox.headlinecaption = FindHeadlineSizeForJumpStories(_newbox);
                _hlarea = _newbox.headlinewidth * _newbox.headlinelength;
            }

            using (ArticleItem _item = _position.layout.GetKicketItem())
            {
                Node _n = GetPositionNode(_item);
                _newbox.kickerPosition = _n;
                _newbox.kickerlength = (_n != null ? _n.length : 0);
                _kickerarea = (_n != null ? _n.length * _n.width : 0);
            }

            using (ArticleItem _item = _position.layout.GetBylineItem())
            {
                Node _n = GetPositionNode(_item);
                _newbox.bylinePosition = _n;
                _newbox.byline = (_n != null ? _n.length : 0);
                _bylinearea = (_n != null ? _n.length * _n.width : 0);
            }
            using (ArticleItem _item = _position.layout.GetPreambleItem())
            {
                Node _n = GetPositionNode(_item);
                _newbox.preamblePosition = _n;
                _newbox.preamble = (_n != null ? _n.length : 0);
                _preamblearea = (_n != null ? _n.length * _n.width : 0);
            }

            int _imagecounter = 0;
            foreach (var _imageitem in _position.layout.GetImageItem())
            {
                Image _image = null;

                if (_newbox.imageList != null && _imagecounter < _newbox.imageList.Count)
                    _image = Helper.DeepCloneImage(_newbox.imageList[_imagecounter]);
                else
                    _image = new Image() { id = "", imagetype = "Image", origlength = 1, origwidth = 1 };

                _image.width = int.Parse(_imageitem.width);
                _image.length = int.Parse(_imageitem.height);
                Node _in = new Node() { pos_x = int.Parse(_imageitem.x), pos_z = int.Parse(_imageitem.y), width = _image.width, length = _image.length, isOccupied = true };
                _image.position = _in;

                if (_imageitem.caption != null)
                {
                    Node captionNode = GetPositionNode(_imageitem.caption);
                    _image.captionPosition = captionNode;
                }
                else
                {
                    ImageCaption _imgcaption = (ImageCaption)FlowProClass.lstImageCaption[_image.id + "/" + _newbox.Id];
                    _image.captionlength = _imgcaption.getlines(_image.width) + ModelSettings.extraimagecaptionline;
                    Node captionNode = new Node() { pos_x = _image.position.pos_x, pos_z = _image.position.pos_z + _image.length, width = _image.position.width, length = _image.captionlength };
                    _image.captionPosition = captionNode;
                }
                _newbox.usedImageList.Add(_image);
                _imagecounter++;
                _imagearea += (_image.width * _image.length) + (_image.captionPosition.width * _image.captionPosition.length);
            }
            _imagecounter = 0;
            foreach (var _imageitem in _position.layout.GetFactboxItem())
            {
                Image _image = null;
                if (_newbox.factList != null && _imagecounter < _newbox.factList.Count)
                    _image = Helper.DeepCloneImage(_newbox.factList[_imagecounter]);
                else
                    _image = new Image() { id = "", imagetype = "FactBox", origlength = 1, origwidth = 1 };

                _image.width = int.Parse(_imageitem.width);
                _image.length = int.Parse(_imageitem.height);
                _image.captionlength = 0;
                Node _in = new Node() { pos_x = int.Parse(_imageitem.x), pos_z = int.Parse(_imageitem.y), width = _image.width, length = _image.length, isOccupied = true };
                _image.position = _in;
                Node captionNode = new Node() { pos_x = _image.position.pos_x, pos_z = _image.position.pos_z + _image.length, width = _image.position.width, length = 0 };
                _image.captionPosition = captionNode;
                _newbox.usedImageList.Add(_image);
                _imagecounter++;
                _imagearea += _image.width * _image.length;
            }

            _imagecounter = 0;
            foreach (var _imageitem in _position.layout.GetCitationItem())
            {
                Image _image = null;
                if (_newbox.citationList != null && _imagecounter < _newbox.citationList.Count)
                    _image = Helper.DeepCloneImage(_newbox.citationList[_imagecounter]);
                else
                    _image = new Image() { id = "", imagetype = "Citation", origlength = 1, origwidth = 1 };
                _image.width = int.Parse(_imageitem.width);
                _image.length = int.Parse(_imageitem.height);
                _image.captionlength = 0;
                Node _in = new Node() { pos_x = int.Parse(_imageitem.x), pos_z = int.Parse(_imageitem.y), width = _image.width, length = _image.length, isOccupied = true };
                _image.position = _in;
                Node captionNode = new Node() { pos_x = _image.position.pos_x, pos_z = _image.position.pos_z + _image.length, width = _image.position.width, length = 0 };
                _image.captionPosition = captionNode;
                _newbox.usedImageList.Add(_image);
                _imagecounter++;
                _imagearea += _image.width * _image.length;
            }

            //Calculate total article area required to decide whether this article needs jump
            int _boxarea = (int)(_newbox.width * _newbox.length);
            _totalarticlearea = (int)_newbox.origArea + _hlarea + _imagearea + _kickerarea + _preamblearea + _bylinearea;

            if (_totalarticlearea <= _boxarea + ModelSettings.jumpArticleSettings.linesofOversetAllowed)
            {
                _newbox.jumpTowidth = 0;
                _newbox.jumpTolength = 0;
                jumparticle.origArea = 0;
                jumparticle.preamble = 0;
                jumparticle.byline = 0;
            }
            else
            {
                if (_preamblearea > 0)
                    jumparticle.preamble = 0;
                if (_bylinearea > 0)
                    jumparticle.byline = 0;

                Jumps _jumps = dictJumpSettings[jumparticle.Id];
                int _jumptoline = 0;
                int _jumpfromline = 0;
                JumpLine _jumpline = _jumps.lstJumpTo.Find(x => x.column == 1);
                if (_jumpline != null)
                    _jumptoline = _jumpline.lines;

                _newbox.jumpTowidth = 1;
                _newbox.jumpTolength = _jumptoline;

                JumpLine _fromjumpline = _jumps.lstJumpFrom.Find(x => x.column == 1);
                if (_fromjumpline != null)
                    _jumpfromline = _fromjumpline.lines;

                //set the correct origarea to the main article
                int _textlines = _boxarea - (_imagearea + _hlarea + _jumptoline + _preamblearea + _bylinearea + _kickerarea);
                jumparticle.origArea = jumparticle.origArea - _textlines;
                jumparticle.origArea += _jumpfromline * 1;
                jumparticle.jumpFromlength = _jumpfromline;
                jumparticle.jumpFromwidth = 1;
            }
            //Remove the images from the main article
            if (_position.layout.GetImageItem() != null && _position.layout.GetImageItem().Count > 0 && jumparticle.imageList != null)
            {
                if (_position.layout.GetImageItem().Count <= jumparticle.imageList.Count)
                    jumparticle.imageList.RemoveRange(0, _position.layout.GetImageItem().Count);
                else
                    jumparticle.imageList.Clear();
            }
            if (_position.layout.GetFactboxItem() != null && _position.layout.GetFactboxItem().Count > 0 && jumparticle.factList != null)
            {
                if (_position.layout.GetFactboxItem().Count <= jumparticle.factList.Count)
                    jumparticle.factList.RemoveRange(0, _position.layout.GetFactboxItem().Count);
                else
                    jumparticle.factList.Clear();
            }

            if (_position.layout.GetKicketItem() != null && jumparticle.citationList != null)
            {
                jumparticle.citationList = null;
            }

            _articlelist.Add(_newbox);
        }
        _sc = new ScoreList(_articlelist, 0);
        return _sc;
    }


    private Node GetPositionNode(ArticleItem _item)
    {
        if (_item == null)
            return null;

        Node n = new Node() { pos_x = int.Parse(_item.x), pos_z = int.Parse(_item.y), width = int.Parse(_item.width), length = int.Parse(_item.height) };

        return n;
    }
    private void SetTheJumpFromToPageIds(List<Box> filteredarticles)
    {
        foreach (var _jump in filteredarticles.Where(x => x.isjumparticle))
        {
            var _section = _jump.jumpSection;
            if (lstPages.Count(x => x.bFrontPage && x.section.Equals(_section, StringComparison.OrdinalIgnoreCase)) == 1)
            {
                PageInfo frontPage = lstPages.First(x => x.bFrontPage && x.section.Equals(_section, StringComparison.OrdinalIgnoreCase));
                ScoreList sc = frontPage.sclist;
                if (sc != null && sc.boxes != null && sc.boxes.Exists(x => x.Id == _jump.Id))
                {
                    Box frontPageArticle = sc.boxes.First(x => x.Id == _jump.Id);
                    string topageid = _jump.pagesname + _jump.page;
                    _jump.jumpfrompageid = frontPage.sname + frontPage.pageid;
                    frontPageArticle.jumptopageid = topageid;
                }
            }
        }

    }

    private void SetOversetRules()
    {

        if (!ModelSettings.enableTextOverset)
            return;

        foreach (var rule in ModelSettings.oversetRules.sectionrules)
        {
            if (rule.sections.Count == 0)
            {
                if (rule.priority.Count == 0)
                {
                    articles.Where(x => x.origArea >= ModelSettings.oversetRules.minTextLines).ToList().ForEach(x => x.allowoverset = true);
                }
                else
                {
                    foreach (var _priority in rule.priority)
                        articles.Where(x => x.origArea >= ModelSettings.oversetRules.minTextLines && x.priority == Helper.GetNumericalPriority(_priority)).ToList().ForEach(x => x.allowoverset = true);
                }
            }
            else
            {
                foreach (var _section in rule.sections)
                {
                    if (rule.priority.Count == 0)
                    {
                        articles.Where(x => x.origArea >= ModelSettings.oversetRules.minTextLines && x.category.Equals(_section, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(x => x.allowoverset = true);
                    }
                    else
                    {
                        foreach (var _priority in rule.priority)
                            articles.Where(x => x.origArea >= ModelSettings.oversetRules.minTextLines && x.priority == Helper.GetNumericalPriority(_priority)
                                && x.category.Equals(_section, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(x => x.allowoverset = true);
                    }
                }
            }
        }
    }

    private void FindAllOversetSizes(Box _box, List<Box> _boxlist)
    {
        List<Box> oversetboxes = new List<Box>();
        foreach (var _boxitem in _boxlist)
        {
            int _length = (int)_boxitem.length;
            int _width = (int)_boxitem.width;
            double _minarea = _boxitem.origArea * (100 - ModelSettings.oversetRules.oversetPercentage) / 100;
            int _minlength = (int)Math.Ceiling(_minarea / _width);
            for (int _tlength = _length - 1; _tlength >= _minlength; _tlength--)
            {
                if (_tlength < _boxitem.preamble)
                    continue;
                Box _newbox = Helper.CustomCloneBox(_boxitem);
                _newbox.length = _tlength;
                _newbox.oversetarea = (_length - _tlength) * _width;
                _newbox.origArea = _boxitem.origArea - _newbox.oversetarea;
                oversetboxes.Add(_newbox);
            }
        }
        if (oversetboxes.Count > 0)
            _boxlist.AddRange(oversetboxes);
    }

    private void LoadRewardsFromModeltuning(JsonElement root)
    {
        if (root.TryGetProperty("rewards", out var rewardsElement))
        {
            if (rewardsElement.TryGetProperty("articleimagesizegreaterthan", out var articleimagesizegreaterthanElement))
            {
                foreach (var rewardElement in articleimagesizegreaterthanElement.EnumerateArray())
                {
                    int _si = rewardElement.GetProperty("width").GetInt32();
                    int _re = rewardElement.GetProperty("reward").GetInt32();

                    JsonElement priorityElement = rewardElement.GetProperty("priority");
                    ModelSettings.rewards.lstarticleimagesizeReward = new List<Rewards.articleimagesizegreatherthan>();
                    foreach (var rewardpriorityElement in priorityElement.EnumerateArray())
                    {
                        int _pi = Helper.GetNumericalPriority(rewardpriorityElement.GetString());
                        if (!ModelSettings.rewards.lstarticleimagesizeReward.Exists(x => x.priority == _pi && x.size == _si))
                            ModelSettings.rewards.lstarticleimagesizeReward.Add(new Rewards.articleimagesizegreatherthan() { priority = _pi, reward = _re, size = _si });

                    }
                    if (priorityElement.GetArrayLength() == 0)
                    {
                        foreach (string _spriority in lstArticlePriority)
                        {
                            int _pi = Helper.GetNumericalPriority(_spriority);
                            if (!ModelSettings.rewards.lstarticleimagesizeReward.Exists(x => x.priority == _pi && x.size == _si))
                                ModelSettings.rewards.lstarticleimagesizeReward.Add(new Rewards.articleimagesizegreatherthan() { priority = _pi, reward = _re, size = _si });
                        }
                    }

                }
            }

            if (rewardsElement.TryGetProperty("headlinetypolinesgreaterthan", out var headlinetypolinesgreaterthanElement))
            {
                foreach (var rewardElement in headlinetypolinesgreaterthanElement.EnumerateArray())
                {
                    int _tl = rewardElement.GetProperty("typolines").GetInt32();
                    int _re = rewardElement.GetProperty("reward").GetInt32();

                    JsonElement priorityElement = rewardElement.GetProperty("priority");
                    ModelSettings.rewards.lsthltypolineReward = new List<Rewards.headlinetypolinesgreaterthan>();
                    foreach (var rewardpriorityElement in priorityElement.EnumerateArray())
                    {
                        int _pi = Helper.GetNumericalPriority(rewardpriorityElement.GetString());
                        if (!ModelSettings.rewards.lsthltypolineReward.Exists(x => x.priority == _pi))
                            ModelSettings.rewards.lsthltypolineReward.Add(new Rewards.headlinetypolinesgreaterthan() { priority = _pi, reward = _re, typolines = _tl });

                    }
                    if (priorityElement.GetArrayLength() == 0)
                    {
                        foreach (string _spriority in lstArticlePriority)
                        {
                            int _pi = Helper.GetNumericalPriority(_spriority);
                            if (!ModelSettings.rewards.lsthltypolineReward.Exists(x => x.priority == _pi))
                                ModelSettings.rewards.lsthltypolineReward.Add(new Rewards.headlinetypolinesgreaterthan() { priority = _pi, reward = _re, typolines = _tl });
                        }
                    }

                }
            }

            if (rewardsElement.TryGetProperty("fixedwidthimages", out var fixedwidthimagesElement))
            {
                foreach (var rewardElement in fixedwidthimagesElement.EnumerateArray())
                {
                    int _re = rewardElement.GetProperty("reward").GetInt32();
                    JsonElement priorityElement = rewardElement.GetProperty("priority");
                    ModelSettings.rewards.lstFixedWidthImage = new List<Rewards.fixedWidthImageSizes>();
                    foreach (var rewardpriorityElement in priorityElement.EnumerateArray())
                    {
                        int _pi = Helper.GetNumericalPriority(rewardpriorityElement.GetString());
                        if (!ModelSettings.rewards.lstFixedWidthImage.Exists(x => x.priority == _pi))
                            ModelSettings.rewards.lstFixedWidthImage.Add(new Rewards.fixedWidthImageSizes() { priority = _pi, reward = _re });

                    }
                    if (priorityElement.GetArrayLength() == 0)
                    {
                        foreach (string _spriority in lstArticlePriority)
                        {
                            int _pi = Helper.GetNumericalPriority(_spriority);
                            if (!ModelSettings.rewards.lstFixedWidthImage.Exists(x => x.priority == _pi))
                                ModelSettings.rewards.lstFixedWidthImage.Add(new Rewards.fixedWidthImageSizes() { priority = _pi, reward = _re });
                        }
                    }

                }
            }

            if (rewardsElement.TryGetProperty("layoutpreference", out var layoutpreferenceElement))
            {
                ModelSettings.rewards.layoutpreference = layoutpreferenceElement.Deserialize<Rewards.Layoutpreference>();
            }

            if (rewardsElement.TryGetProperty("RewardOnArticleWidthPreference", out var rowp))
            {
                ModelSettings.rewards.rewardOnArticleWidthPreference = rowp.Deserialize<Rewards.RewardOnArticleWidthPreference>();
            }
        }
    }

    private string FindHeadlineSizeForJumpStories(Box _newbox)
    {
        string hlsize = "";
        int _width = _newbox.headlinePosition.width;
        int _height = _newbox.headlinePosition.length;

        Headline _headline = (Headline)headlines[_newbox.Id];
        List<string> _headlines = new List<string>() { "large", "medium", "small" };

        foreach (string _hlsize in _headlines)
        {
            int _headlinelength = _headline.GetHeadlineHeight(_hlsize, (int)_newbox.width);
            if (_headlinelength == _height)
            {
                hlsize = _hlsize;
                break;
            }
        }
        //If we aren't able to find headline size based on the exact length match then find the size that's < height provide
        if (hlsize == "")
        {
            foreach (string _hlsize in _headlines)
            {
                int _headlinelength = _headline.GetHeadlineHeight(_hlsize, (int)_newbox.width);
                if (_headlinelength < _height)
                {
                    hlsize = _hlsize;
                    break;
                }
            }
        }

        if (hlsize != "")
            return hlsize;
        else
            return ModelSettings.jumpArticleSettings.defaultHeadline;
    }

    private void PlaceJumpArticleItem(IAutomationPageArticleItems _item, Node _node)
    {
        _item.x = _node.pos_x;
        _item.y = _node.pos_z;
        _item.width = _node.width;
        _item.height = _node.length;
    }

    private void PlacePictureLeadArticleItem(IAutomationPageArticleItems _item, Node _node, int _x, int _y)
    {
        _item.x = _node.pos_x + _x;
        _item.y = _node.pos_z + _y;
        _item.width = _node.width;
        _item.height = _node.length;
    }
    void ParseImageSoftCrops(string cropInfo, Image image)
    {
        string crop = cropInfo;
        if (!cropInfo.StartsWith(cropIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Invalid crop info.");
            return;
        }
        crop = crop.Remove(0, cropIdentifier.Length);
        string[] croppedvalue = crop.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (croppedvalue.Length != 4)
        {
            Log.Warning("Not all the crop values provided.");
            return;
        }

        foreach (string val in croppedvalue)
            if (!double.TryParse(val, out _))
            {
                Log.Warning("Not all the crop values are in number format");
                return;
            }

        double left = double.Parse(croppedvalue[0]);
        double top = double.Parse(croppedvalue[1]);
        double width = double.Parse(croppedvalue[2]);
        double height = double.Parse(croppedvalue[3]);

        image.softcropImage = new SoftCropImage(left, top, width, height);
    }

    private void SetSoftCropImageDimensions(Image _image)
    {
        if (_image.softcropImage == null)
            return;

        _image.softcropImage.originalwidth = _image.origwidth;
        _image.softcropImage.originalheight = _image.origlength;
        _image.origwidth = (int)(_image.softcropImage.width * (double)_image.softcropImage.originalwidth);
        _image.origlength = (int)(_image.softcropImage.height * (double)_image.softcropImage.originalheight);
    }


    private void BuildJumpFrontPage()
    {
        try
        {
            Log.Information("Building Jump Front Page");
            foreach (var _jumpsection in ModelSettings.jumpArticleSettings.jumpSections)
            {
                Log.Information("Building Jump Front Page for {sect}", _jumpsection);
                List<Box>? filteredarticles = null;
                List<PageInfo>? lstFilteredPages = null;

                if (articles.Count(arts => arts.jumpSection != null && arts.jumpSection.Equals(_jumpsection, StringComparison.OrdinalIgnoreCase)) > 0)
                    filteredarticles = articles.Where(arts => arts.jumpSection != null && arts.jumpSection.Equals(_jumpsection, StringComparison.OrdinalIgnoreCase)).ToList();
                if (lstPages.Count(sect => sect.section.Equals(_jumpsection, StringComparison.OrdinalIgnoreCase) && !sect.ignorepage) > 0)
                    lstFilteredPages = lstPages.Where(sect => sect.section.Equals(_jumpsection, StringComparison.OrdinalIgnoreCase) && !sect.ignorepage).OrderBy(x => x.sname).ThenBy(x => x.pageid).ToList();

                if (lstFilteredPages == null || lstFilteredPages.Count() == 0)
                {
                    Log.Information("BuildLayout - No pages Found for Section: {section}", _jumpsection);
                    continue;
                }

                if (filteredarticles == null || filteredarticles.Count == 0)
                {
                    Log.Information("BuildLayout - No articles Found for Section: {section}", _jumpsection);
                    continue;
                }

                lstFilteredPages[0].bFrontPage = true;
                //Set the priority of JumpArticle to be 0
                filteredarticles.ForEach(x => x.priority = 0);
                Log.Information("BuildLayout - Processing the Jump for: {section}", _jumpsection);

                ScoreList _frontpagescore = null;
                try
                {
                    _frontpagescore = GenerateJumpFrontPage(filteredarticles);
                    lstFilteredPages[0].sclist = _frontpagescore;
                }
                catch (Exception ex)
                {
                    Log.Error("BuildLayout - Error found during GenerateJumpFrontPage: {error}", ex.StackTrace);
                }
                //_frontJumpPage = lstFilteredPages[0];

                ////Move the IsNewPage Flag to the next article if the jumparticle had isnewpageflag and needs to be removed
                //for (int i = 0; i < filteredarticles.Count; i++)
                //{
                //    if (filteredarticles[i].isjumparticle && filteredarticles[i].origArea <= 0 && filteredarticles[i].isNewPage)
                //    {
                //        if (i + 1 < filteredarticles.Count && !filteredarticles[i + 1].isNewPage)
                //            filteredarticles[i + 1].isNewPage = true;
                //    }
                //}
                ////Remove the articles where no text area left
                //filteredarticles.RemoveAll(x => x.isjumparticle && x.origArea <= 0);
                //lstFilteredPages.RemoveAt(0);

            }
        }
        catch (Exception e)
        {
            Log.Error("Error while building Jump Front Page: {err}", e.StackTrace);
        }
    }

    private List<Box> BuildPictureLeadAboveHeadline(Box _box, ModelSettings.PictureLeadArticleType aboveHeadline)
    {
        int _kickerlength = 0;

        List<Box> lstboxes = new List<Box>();
        List<Image> mainImgSizes = Image.GetAllPossibleImageSizes(_box.imageList[0], _box, 1);
        Headline _headline = (Headline)headlines[_box.Id];
        foreach (var size in aboveHeadline.size)
        {
            if (kickersmap.Count > 0 && kickersmap[_box.Id] != null)
                _kickerlength = (int)((Kicker)kickersmap[_box.Id]).collinemap[size];
            int _articleheight = (int)Math.Ceiling(_box.origArea / size);
            if (_articleheight < _box.byline + _box.preamble)
                _articleheight = _box.byline + _box.preamble;

            foreach (var _imgsize in mainImgSizes.Where(x => x.width == size))
            {
                int _prevhlheight = 0;
                foreach (var _hlsize in aboveHeadline.headline)
                {
                    int _hlheight = _headline.GetHeadlineHeight(_hlsize, size);
                    if (_hlheight == _prevhlheight)
                        continue;

                    _prevhlheight = _hlheight;
                    int _y = 0;
                    Box _newBox = Helper.CustomCloneBox(_box);
                    Image _newimage = Helper.CustomCloneImage(_imgsize);
                    _newBox.articletype = "picturelead";
                    _newBox.width = size;
                    _newBox.length = _articleheight + _newimage.length + _hlheight + _kickerlength;
                    _newBox.headlinewidth = size;
                    _newBox.headlinelength = _hlheight;
                    _newBox.headlinecaption = _hlsize;
                    _newBox.kickerlength = _kickerlength;
                    if (_kickerlength > 0)
                        _newBox.kickerPosition = new Node(0, 0, size, _kickerlength);
                    _newimage.position = new Node(0, 0 + _kickerlength, _newimage.width, _newimage.length);
                    _newBox.usedImageList = new List<Image>() { _newimage };
                    _y += _newimage.length;
                    _newBox.headlinePosition = new Node(0, _newimage.position.pos_z + _newimage.length, size, _hlheight);
                    if (_newBox.preamble > 0)
                        _newBox.preamblePosition = new Node(0, _newBox.headlinePosition.pos_z + _hlheight, 1, _newBox.preamble);
                    if (_newBox.byline > 0)
                        _newBox.bylinePosition = new Node(0, _newBox.headlinePosition.pos_z + _hlheight + _newBox.preamble, 1, _newBox.byline);

                    _newBox.volume = _newBox.width * _newBox.length;
                    _newBox.usedimagecount = 1;
                    lstboxes.Add(_newBox);
                }

            }
        }
        return lstboxes;
    }

    private List<Box> BuildPictureLeadPartialHeadline(Box _box, ModelSettings.PictureLeadArticleType partialHeadline)
    {
        int _kickerlength = 0;

        List<Box> lstboxes = new List<Box>();
        List<Image> mainImgSizes = Image.GetAllPossibleImageSizes(_box.imageList[0], _box, 1);
        Headline _headline = (Headline)headlines[_box.Id];
        foreach (var size in partialHeadline.size)
        {
            if (kickersmap.Count > 0 && kickersmap[_box.Id] != null)
                _kickerlength = (int)((Kicker)kickersmap[_box.Id]).collinemap[size];

            foreach (var hlwidth in partialHeadline.hlsize)
            {
                if (size - hlwidth < 2)
                    continue;
                int _prevhlheight = 0;
                foreach (var _hlsize in partialHeadline.headline)
                {
                    int _hlheight = _headline.GetHeadlineHeight(_hlsize, hlwidth);
                    if (_hlheight == _prevhlheight)
                        continue;

                    _prevhlheight = _hlheight;
                    foreach (var _imgsize in mainImgSizes.Where(x => x.width == size - hlwidth))
                    {
                        int _articleheight = 0;// _imgsize.length ;
                        double _totalarea = _box.origArea + (_hlheight * hlwidth) + (_imgsize.length * _imgsize.width);
                        _articleheight = (int)Math.Ceiling(_totalarea / size);
                        if (_articleheight < _imgsize.length)
                            _articleheight = _imgsize.length;
                        //int _articlehlheight = (int)Math.Ceiling((_box.origArea + (_hlheight * hlwidth)) / hlwidth);
                        //if (_articlehlheight > _articleheight)
                        //    continue;
                        if (_articleheight - _hlheight - _box.byline - _box.preamble < 0)
                            continue;

                        Box _newBox = Helper.CustomCloneBox(_box);
                        Image _newimage = Helper.CustomCloneImage(_imgsize);
                        //_newimage.length = _newimage.length - 1;
                        //_newimage.captionlength = _newimage.captionlength - 1;

                        _newBox.articletype = "picturelead";
                        _newBox.width = size;
                        _newBox.length = _articleheight + _kickerlength;
                        _newBox.headlinewidth = hlwidth;
                        _newBox.headlinelength = _hlheight;
                        _newBox.headlinecaption = _hlsize;
                        _newBox.kickerlength = _kickerlength;
                        if (_kickerlength > 0)
                            _newBox.kickerPosition = new Node(0, 0, size, _kickerlength);
                        _newimage.position = new Node(hlwidth, 0 + _kickerlength, _newimage.width, _newimage.length);
                        _newBox.usedImageList = new List<Image>() { _newimage };

                        _newBox.headlinePosition = new Node(0, _kickerlength, hlwidth, _hlheight);
                        if (_newBox.preamble > 0)
                            _newBox.preamblePosition = new Node(0, _newBox.headlinePosition.pos_z + _hlheight, 1, _newBox.preamble);
                        if (_newBox.byline > 0)
                            _newBox.bylinePosition = new Node(0, _newBox.headlinePosition.pos_z + _hlheight + _newBox.preamble, 1, _newBox.byline);
                        _newBox.volume = _newBox.width * _newBox.length;
                        _newBox.usedimagecount = 1;
                        lstboxes.Add(_newBox);
                    }

                }
            }
        }
        return lstboxes;
    }

    private AutomationPageArticle GeneratePictureLeadJson(Box _article)
    {
        AutomationPageArticle article = new AutomationPageArticle();
        article.article_id = _article.Id;
        article.x = (int)_article.position.pos_x;
        article.y = (int)_article.position.pos_z;
        article.height = (int)_article.length;
        article.width = (int)_article.width;
        article.slug = "";
        article.headline = headlineMap.ContainsKey(_article.Id) ? headlineMap[_article.Id].ToString() : "";

        ArrayList _pageitems = new ArrayList();

        //add headline
        if (_article.headlinePosition != null)
        {
            AutomationPageArticleHeadline _headlineitem = new AutomationPageArticleHeadline();
            PlacePictureLeadArticleItem(_headlineitem, _article.headlinePosition, article.x, article.y);
            _headlineitem.type = "headline";
            _headlineitem.size = _article.headlinecaption;
            _pageitems.Add(_headlineitem);
        }

        if (_article.kickerPosition != null)
        {
            AutomationPageArticleHeadline _kickeritem = new AutomationPageArticleHeadline();
            PlacePictureLeadArticleItem(_kickeritem, _article.kickerPosition, article.x, article.y);
            _kickeritem.type = "kicker";
            _kickeritem.size = null;
            _pageitems.Add(_kickeritem);
        }

        if (_article.preamblePosition != null)
        {
            AutomationPageArticleDeck _deck = new AutomationPageArticleDeck();
            PlacePictureLeadArticleItem(_deck, _article.preamblePosition, article.x, article.y);
            _deck.type = "deck";
            _pageitems.Add(_deck);
        }
        //Adding byline

        if (_article.bylinePosition != null)
        {
            AutomationPageArticleByline _byline = new AutomationPageArticleByline();
            PlacePictureLeadArticleItem(_byline, _article.bylinePosition, article.x, article.y);
            _byline.type = "byline";
            _pageitems.Add(_byline);
        }


        if (_article.usedImageList != null && _article.usedImageList.Count > 0)
        {
            foreach (var image in _article.usedImageList)
            {
                _pageitems.Add(Helper.GetImageItem(image, image.position.pos_x + article.x, image.position.pos_z + article.y, _article.Id));
            }
        }

        article.items = _pageitems;
        return article;
    }
}
