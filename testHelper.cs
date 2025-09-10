using Microsoft.Extensions.Logging.Abstractions;
using NavigaFlowPro.PngGenerator;
using Serilog;
using System.Numerics;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavigaFlowPro.App;

public class Helper
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        Converters = { new BigIntegerConverter() }
    };

    public static int BoxCanbeFittedInPage(int _pagearea, int _pagelength, Box _article)
    {
        int retvalue = 0;
        int _minarea = _article._possibleAreas.Min(x => x.Value);

        if (_minarea <= _pagearea)
        {
            retvalue = _minarea;
        }
        else if (_article.articletype == "briefs" && ModelSettings.briefSettings.overset)
        {
            retvalue = _minarea;
        }
        
        return retvalue;
    }

    public static List<Box> GetRemainingLowitems(List<ScoreList> _list, String _section, List<Box> _lowlist, HashSet<String> _articlelist, int _mandatoryOrder)
    {
        List<Box> _boxlist = null;
        if (_mandatoryOrder == 1)
        {
            _boxlist = new List<Box>();
            foreach (String _article in _articlelist)
            {
                var _temparticle = _lowlist.Where(x => x.Id == _article).ToList();
                if (_temparticle.Count > 0)
                    _boxlist.Add(Helper.DeepCloneBox(_temparticle[0]));
            }
        }
        else
        {
            _boxlist = Helper.DeepCloneListBox(_lowlist);// articles.Where(cust => cust.priority <= 3 && cust.category == _section).ToList();
            for (int _i = 0; _i < _list.Count(); _i++)
            {
                if (_list[_i].boxes != null)
                    foreach (Box _box in _list[_i].boxes)
                        _boxlist.RemoveAll(x => x.Id == _box.Id);
            }
        }
        return _boxlist;
    }

    public static List<Box> GetRemainingHighitems(List<ScoreList> _list, String _section, List<Box> _highlist, HashSet<String> _articlelist, int _mandatoryOrder)
    {
        List<Box> _boxlist = null;

        if (_mandatoryOrder == 1)
        {
            _boxlist = new List<Box>();
            foreach (String _article in _articlelist)
            {
                var _temparticle = _highlist.Where(x => x.Id == _article).ToList();
                if (_temparticle.Count > 0)
                    _boxlist.Add(Helper.DeepCloneBox(_temparticle[0]));
            }
        }
        else
        {
            _boxlist = Helper.DeepCloneListBox(_highlist);// articles.Where(cust => cust.priority >= 4 && cust.category == _section).ToList();
            for (int _i = 0; _i < _list.Count(); _i++)
            {
                if (_list[_i].boxes != null)
                    foreach (Box _box in _list[_i].boxes)
                        _boxlist.RemoveAll(x => x.Id == _box.Id);
            }
        }
        return _boxlist;
    }

    public static bool isValidBox(Box box, int ipreamble)
    {
        var images = box.usedImageList;
        if (images == null || images.Count == 0)
            return true;

        var mainImage = images.Find(x => x.mainImage == 1);
        int totalImageLength = 0;

        var mugshots = images.Where(img => img.imagetype == "mugshot").ToList();

        if (mugshots.Any())
        {
            var nonMugshots = images.Where(img => img.imagetype != "mugshot").ToList();
            int availableWidth = (int)box.width - ipreamble;

            bool hasOversizedImage = nonMugshots
                .Where(img => !img.aboveHeadline)
                .Any(img => img.width > availableWidth);

            if (hasOversizedImage)
            {
                return false;
            }
            var maxLen = mugshots.Max(x => x.imageMetadata.doubleSize.Length);
            if (box.width == 1 || box.length < maxLen)
            {
                //For box width 1 we will not be supporting mugshots
                return false;
            }
        }

        // MainImage is Above headline either fullwidth or not.
        totalImageLength += (mainImage != null && mainImage.aboveHeadline)
            ? mainImage.length
            : 0;

        // Above headline subimages, stacked just below mainImage.
        totalImageLength += (mainImage != null && mainImage.aboveHeadline && mainImage.width == box.width)
            ? images
                .Where(img => img.aboveHeadline && img.id != mainImage.id)
                .Select(img => img.length)
                .DefaultIfEmpty(0)
                .Max()
            : 0;

        // fullwidth subimages
        totalImageLength += images.Where(img => img.width == box.width && (mainImage == null || img.id != mainImage.id)).Sum(img => img.length);

        int requiredTextLength = box.preamble + box.byline;
        int availableLength = (int)box.length - totalImageLength;
        return availableLength >= requiredTextLength;
    }
    
    public static bool isValidFact(Image factImg)
    {
        int minHeight = ModelSettings.multiColumnFactMinHeights.ContainsKey(factImg.width) ? ModelSettings.multiColumnFactMinHeights[factImg.width] : ModelSettings.multiColumnFactMinLines;
        if (factImg.width > 1 && factImg.length < minHeight)
        {
            return false;
        }
        return true;
    }

    public static BigInteger[] calculateScore(List<Box> boxes, bool penalty, PageInfo _pinfo)
    {
        double sc = 0;
        BigInteger[] arr = new BigInteger[2];
        BigInteger uniqueid = 0;
        int _amainsize = 0;
        int _bmainsize = 0;
        foreach (Box b in boxes)
        {
            double _area = 0;
            double _bsc = 0;
            int _imagecount = 0;
            if (b.position != null)
            {
                _area += b.origArea; //includes text, byline, preamble
                _area += b.width * (b.kickerlength);
                _area += b.headlinewidth * (b.headlinelength);
                _bsc += b.origArea;
                _bsc += b.headlinewidth * (b.headlinelength);
                _bsc += b.width * (b.kickerlength);

                if (_pinfo.ads != null && _pinfo.ads.Count > 0)
                {
                    if (_pinfo.ads.Where(x => x.newx == b.position.pos_x + b.width).Count() > 0)
                    {
                        var _ads = _pinfo.ads.Where(x => x.newx == b.position.pos_x + b.width).OrderBy(x => x.newy).First();
                        if (_ads != null && b.position.pos_z < _ads.newy - 1)
                            _bsc = _bsc - 10;
                    }
                }

                if (b.usedImageList != null && b.usedImageList.Count() > 0)
                {
                    _imagecount = b.usedImageList.Count(x => x.imagetype == "Image");
                    foreach (Image _img in b.usedImageList)
                    {
                        if (_img.mainImage == 1)
                        {
                            if (b.priority == 5)
                                _amainsize = _img.width;
                            else
                            {
                                _bmainsize = _img.width > _bmainsize ? _img.width : _bmainsize;
                            }

                            //Applying Rewards
                            if (ModelSettings.rewards.lstarticleimagesizeReward != null)
                            {
                                foreach (var _imagesizegreaterthan in ModelSettings.rewards.lstarticleimagesizeReward.Where(x => x.priority == b.priority))
                                {
                                    if (_img.width > _imagesizegreaterthan.size)
                                    {
                                        _bsc += _imagesizegreaterthan.reward;
                                        break;
                                    }
                                }
                            }

                        }

                        if (ModelSettings.rewards.lstFixedWidthImage != null && ModelSettings.rewards.lstFixedWidthImage.Count > 0
                            && _img.fixedWidthImage)
                        {
                            foreach (var _fixedwidthimage in ModelSettings.rewards.lstFixedWidthImage.Where(x => x.priority == b.priority))
                            {
                                _bsc += _fixedwidthimage.reward;
                                break;
                            }
                        }

                        var imgArea = (_img.imagetype == "mugshot") ? _img.imageMetadata.doubleSize.Area : _img.width * _img.length;
                        _area += imgArea;
                        if (b.priority == 5 && _img.mainImage == 1 && _img.width == 1)
                        {
                            //Not for mugshot 
                            if (!_img.fixedWidthImage)
                                _bsc += _img.width * _img.length - 5;
                        }
                        else
                            _bsc += imgArea;
                    }
                }

                if (ModelSettings.rewards.layoutpreference != null && _pinfo != null && _pinfo.pageid>0
                    && boxes.Count==1)
                {
                    if (_pinfo.pageid % 2 == 0)
                    {
                        var opref = ModelSettings.rewards.layoutpreference;
                        if (_imagecount >= opref.minimumimagesinlayout && opref.evenpagelayout.Equals(b.layouttype.ToString(), StringComparison.OrdinalIgnoreCase))
                            _bsc += opref.reward;
                    }
                    else
                    {
                        var opref = ModelSettings.rewards.layoutpreference;
                        if (_imagecount >= opref.minimumimagesinlayout && opref.oddpagelayout.Equals(b.layouttype.ToString(), StringComparison.OrdinalIgnoreCase))
                            _bsc += opref.reward;
                    }
                }

                if(ModelSettings.rewards.lsthltypolineReward != null && ModelSettings.rewards.lsthltypolineReward.Count()>0)
                {
                    if(ModelSettings.rewards.lsthltypolineReward.Exists(x=>x.priority == b.priority && b.headlinetypoline > x.typolines))
                    {
                        _bsc += ModelSettings.rewards.lsthltypolineReward.Find(x => x.priority == b.priority && b.headlinetypoline > x.typolines).reward;
                    }
                }

                if (ModelSettings.lstPenalties.Count > 0)
                {
                    foreach (var oPenalty in ModelSettings.lstPenalties)
                    {
                        if (oPenalty.priority == b.priority && oPenalty.size == b.width)
                            _bsc = _bsc - _bsc * oPenalty.penaltyperc / 100;
                    }
                }
                if (b.oversetarea > 0)
                    _bsc = _bsc * (100 - ModelSettings.oversetRules.oversetPenalty) / 100;
                sc += _bsc; ;

                if (_imagecount > 0 && b.priority >= 4)
                    sc += 5 * (_imagecount);
                else
                    sc += 3 * (_imagecount);

                if (b.articletype != "loft")
                {
                    if (_area > b.length * b.width)
                    {
                        Log.Error("Box contents Area cannot be greater than outer box area: {area} {otherArea}", _area, b.length * b.width);
                        Log.Error("Box Id: {id}", b.Id);
                        foreach (Image _img in b.usedImageList)
                        {
                            Log.Information("{id} {w} {l} {capL}", _img.id, _img.width, _img.length, _img.captionlength);
                        }
                    }
                }
                else
                {
                    if (_area > b.length * b.width)
                    {
                        Log.Information("Box contents Area for LOFT article is greater than outer box area, will be marked as overset {area} {id} {otherArea}", _area, b.Id, b.length * b.width);
                    }
                }
                uniqueid += b.boxorderId;
            }
        }

        if (_amainsize > 0 && _bmainsize > 0 && _bmainsize > _amainsize && !_pinfo.hasQuarterPageAds)
            sc = sc - 10;



        arr[0] = uniqueid;
        arr[1] = (BigInteger) UpdateScoreBasedOnWeightedSumMethod(
            boxes, 
            sc,
            ModelSettings.rewards.rewardOnArticleWidthPreference.MaxWidthPreference,
            ModelSettings.rewards.rewardOnArticleWidthPreference.AverageWidthPreference);
        
        return arr;
    }

    public static double UpdateScoreBasedOnWeightedSumMethod(List<Box> boxes, double originalScore, double alpha, double beta)
    {
        var maxWidthUsed = boxes.Max(x => x.width);
        var avgWidth = boxes.Average(x => (double)x.width);
        var newScore = originalScore + (alpha * maxWidthUsed) + (beta * avgWidth);
        return newScore;
    }

    public static BigInteger[] calculateScore(List<Box> boxes)
    {
        double sc = 0;
        BigInteger[] arr = new BigInteger[2];
        BigInteger uniqueid = 0;
        foreach (Box b in boxes)
        {
            double _area = 0;
            double _bsc = 0;
            if (b.position != null)
            {
                _area += b.origArea; //includes text, byline, preamble
                _area += b.width * b.kickerlength;
                _area += b.headlinewidth * b.headlinelength;
                _bsc = _area;

                if (b.usedImageList != null && b.usedImageList.Count() > 0)
                {
                    foreach (Image _img in b.usedImageList)
                    {
                        _area += _img.width * _img.length;
                        _bsc += _img.width * _img.length;
                        //if (_img.width == 1 & _img.imagetype.ToUpper() == "IMAGE")
                        //    _bsc = _bsc - 2;
                    }
                    int _penalty = (int)(b.usedImageList.Where(x => x.mainImage != 1 && x.imagetype.ToUpper() == "IMAGE" && x.width == 1).Sum(y => y.length) * 0.25);

                    if (_penalty > 0)
                        _bsc = _bsc - _penalty;
                }
                sc += _bsc; //* b.priority;

                if (b.articletype != "loft")
                {
                    if (_area > b.length * b.width)
                    {
                        Log.Error("Box contents Area cannot be greater than outer box area: {area} {otherArea}", _area, b.length * b.width);
                        Log.Error("Box Id: {id}", b.Id);
                        foreach (Image _img in b.usedImageList)
                        {
                            Log.Information("{id} {w} {l} {capL}", _img.id, _img.width, _img.length, _img.captionlength);
                        }
                    }
                }
                else
                {
                    if (_area > b.length * b.width)
                    {
                        Log.Information("Box contents Area for LOFT article is greater than outer box area, will be marked as overset {area} {id} {otherArea}", _area, b.Id, b.length * b.width);
                    }
                }
                uniqueid += b.boxorderId;
            }
        }
        arr[0] = uniqueid;
        arr[1] = (BigInteger)sc;
        return arr;
    }

    public static List<List<Box>> CrossJoin(List<List<Box>> listOfLists)
    {
        List<List<Box>> result = new List<List<Box>>();
        int[] indices = new int[listOfLists.Count];

        while (true)
        {
            List<Box> currentItem = new List<Box>();

            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                currentItem.Add(listOfLists[i][indices[i]]);
            }

            result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
    }

    public static List<List<Box>> CrossJoin(List<List<Box>> listOfLists, int maxArea)
    {
        List<List<Box>> result = new List<List<Box>>();
        int[] indices = new int[listOfLists.Count];


        while (true)
        {
            List<Box> currentItem = new List<Box>();
            double _area = 0;
            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                _area += listOfLists[i][indices[i]].width * listOfLists[i][indices[i]].length;
                currentItem.Add(listOfLists[i][indices[i]]);
            }
            if (_area <= maxArea)
                result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
    }

    public static List<List<Box>> CrossJoin(List<List<Box>> listOfLists, int minArea, int maxArea)
    {
        List<List<Box>> result = new List<List<Box>>();
        int[] indices = new int[listOfLists.Count];


        while (true)
        {
            List<Box> currentItem = new List<Box>();
            double _area = 0;
            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                _area += listOfLists[i][indices[i]].width * listOfLists[i][indices[i]].length;
                currentItem.Add(listOfLists[i][indices[i]]);
            }
            if (_area <= maxArea && _area > minArea)
                result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
        
    }
    public static List<List<double>> CrossJoin(List<List<double>> listOfLists)
    {
        List<List<double>> result = new List<List<double>>();
        int[] indices = new int[listOfLists.Count];

        while (true)
        {
            List<double> currentItem = new List<double>();

            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                currentItem.Add(listOfLists[i][indices[i]]);
            }

            result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
    }

    public static List<List<Image>> CrossJoin(List<List<Image>> listOfLists)
    {
        List<List<Image>> result = new List<List<Image>>();
        int[] indices = new int[listOfLists.Count];

        while (true)
        {
            List<Image> currentItem = new List<Image>();

            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                currentItem.Add(listOfLists[i][indices[i]]);
            }
            result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
    }


    public static List<List<Image>> CrossJoin(List<List<Image>> listOfLists, int minArea, int maxArea)
    {
        List<List<Image>> result = new List<List<Image>>();
        int[] indices = new int[listOfLists.Count];

        while (true)
        {
            List<Image> currentItem = new List<Image>();

            int _area = 0;
            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                _area += listOfLists[i][indices[i]].width * listOfLists[i][indices[i]].length;
                currentItem.Add(listOfLists[i][indices[i]]);
            }
            if (_area >= minArea && _area <= maxArea)
                result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }

        return result;
    }
    public static List<List<Image>> CrossJoinWithHeightValidation(List<List<Image>> listOfLists, int _height)
    {
        List<List<Image>> result = new List<List<Image>>();
        int[] indices = new int[listOfLists.Count];

        while (true)
        {
            List<Image> currentItem = new List<Image>();

            int _th = 0;
            // Add current element from each list
            for (int i = 0; i < listOfLists.Count; i++)
            {
                currentItem.Add(listOfLists[i][indices[i]]);
                _th += listOfLists[i][indices[i]].length;
            }
            if (_th <= _height)
                result.Add(currentItem);

            // Increment the indices
            int j = listOfLists.Count - 1;
            while (j >= 0 && indices[j] == listOfLists[j].Count - 1)
            {
                indices[j] = 0;
                j--;
            }

            if (j < 0)
            {
                break; // All indices have been reset, exit the loop
            }

            indices[j]++;
        }


        return result;
    }
    public static List<ScoreList> DeepCloneListScore(List<ScoreList> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<List<ScoreList>>(json, jsonOptions) ?? [];
    }

    public static List<Box> DeepCloneListBox(List<Box> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<List<Box>>(json, jsonOptions) ?? [];
    }

    public static List<PageAds> DeepCloneListPageAds(List<PageAds> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<List<PageAds>>(json, jsonOptions) ?? [];
    }

    public static List<Image> DeepCloneListImage(List<Image> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<List<Image>>(json, jsonOptions) ?? [];
    }

    public static ImageScoreList DeepCloneImageScoreList(ImageScoreList originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<ImageScoreList>(json, jsonOptions) ?? new();
    }
    public static Dictionary<string, List<Image>> DeepCloneDictionaryImages(Dictionary<string, List<Image>> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<Dictionary<string, List<Image>>>(json, jsonOptions) ?? [];
    }

    public static Dictionary<int, List<Image>> DeepCloneDictionaryImages(Dictionary<int, List<Image>> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<Dictionary<int, List<Image>>>(json, jsonOptions) ?? [];
    }

    public static List<List<Image>> DeepCloneListListImage(List<List<Image>> originalList)
    {
        string json = JsonSerializer.Serialize(originalList, jsonOptions);
        return JsonSerializer.Deserialize<List<List<Image>>>(json, jsonOptions) ?? [];
    }

    public static Image DeepCloneImage(Image image)
    {
        string json = JsonSerializer.Serialize(image, jsonOptions);
        return JsonSerializer.Deserialize<Image>(json, jsonOptions) ?? new();
    }

    public static Box DeepCloneBox(Box box)
    {
        string json = JsonSerializer.Serialize(box, jsonOptions);
        return JsonSerializer.Deserialize<Box>(json, jsonOptions) ?? new();
    }

    public static PageInfo DeepClonePageInfo(PageInfo info)
    {
        string json = JsonSerializer.Serialize(info, jsonOptions);
        return JsonSerializer.Deserialize<PageInfo>(json, jsonOptions) ?? new();
    }

    public static List<FinalScores> RemoveDuplicates(List<FinalScores> originalList)
    {
        List<FinalScores> newList = originalList.OrderByDescending(x => x.totalUniqueId).ThenByDescending(x => x.articlesprinted + x.optionalarticlesprinted).ThenByDescending(y => y.TotalScore).ToList();

        //NT - if count of newList is 1 then we dont need to remove duplicates

        if (newList.Count() == 1)
            return newList;

        System.Numerics.BigInteger _prevuniqueid = 0;
        for (int _i = 0; _i < newList.Count(); _i++)
        {
            if (newList[_i].totalUniqueId == _prevuniqueid)
            {
                newList.RemoveAt(_i);
                _i--;
            }
            else
            {
                _prevuniqueid = newList[_i].totalUniqueId;
            }
        }
        return newList;
    }

    public static AutomationPageArticleItemsDouble GetImageItemD(Image imgDouble, double xpos, double ypos, string boxid)
    {
        if (imgDouble.imageMetadata == null)
            return null;
        if (imgDouble.imageMetadata.doubleSize == null)
            return null;

        if (ModelSettings.mugshotSetting.mugshotAlignment != "left")
        {
            xpos = (xpos + 1) - imgDouble.imageMetadata.doubleSize.Width;
        }

        AutomationPageArticleItemsDouble imgitem = new AutomationPageArticleItemsDouble();
        imgitem.x = xpos;
        imgitem.y = ypos;
        imgitem.width = imgDouble.imageMetadata.doubleSize.Width;
        imgitem.height = imgDouble.imageMetadata.doubleSize.Length - imgDouble.imageMetadata.doubleSize.CaptionLength;
        AutomationImageCaptionDouble _imgcaption = new AutomationImageCaptionDouble()
        {
            type = "caption",
            height = imgDouble.imageMetadata.doubleSize.CaptionLength,
            width = imgDouble.imageMetadata.doubleSize.Width,
            x = xpos,
            y = ypos + imgitem.height
        };

        imgitem.caption = _imgcaption;
        imgitem.image_id = imgDouble.id;
        imgitem.type = "image";
        imgitem.factbox = false;

        imgitem.cropped = false;
        imgitem.article_id = boxid;
        imgitem.original_aspect_ratio = imgDouble.origlength / imgDouble.origwidth;
        return imgitem;
    }

    public static AutomationPageArticleItems GetImageItem(Image _mainImg, int xpos, int ypos, string boxid)
    {
        AutomationPageArticleItems imgitem = new AutomationPageArticleItems();
        imgitem.x = xpos;
        imgitem.y = ypos;
        imgitem.width = _mainImg.width;
        imgitem.height = _mainImg.length - _mainImg.captionlength;
        AutomationImageCaption _imgcaption = new AutomationImageCaption()
        {
            type = "caption",
            height = _mainImg.captionlength,
            width = _mainImg.width,
            x = xpos,
            y = ypos + imgitem.height
        };

        imgitem.caption = _imgcaption;
        imgitem.image_id = _mainImg.id;
        imgitem.type = "image";
        if (_mainImg.imagetype != "Image")
            imgitem.factbox = true;
        else
            imgitem.factbox = false;

        imgitem.cropped = _mainImg.cropped;
        imgitem.article_id = boxid;
        if (_mainImg.imagetype == "Image")
            imgitem.original_aspect_ratio = (double)_mainImg.origwidth / (double)_mainImg.origwidth;
        return imgitem;
    }

    public static bool VerifyHorizontalPlacement(Image mainImg, Image himage, Image vIamge, int _width, int _ipreamble, int _length)
    {
        bool retval = true;

        if (mainImg.width + himage.width <= _width - _ipreamble && (himage.length == mainImg.length - mainImg.captionlength))
        {
            if (_length >= mainImg.length && _length >= himage.length)
            {
                retval = false;
                mainImg.topimageinsidearticle = true;
                himage.topimageinsidearticle = true;
            }
        }

        if (retval) //Both images have to placed vertically
        {
            if (_length >= mainImg.length + himage.length)
            {
                retval = false;
                mainImg.topimageinsidearticle = true;
            }
        }

        return retval;
    }

    public static Image FindSmallestSubImage(Image _subImg, Image _subImg2)
    {
        Image simage = null;

        if (_subImg.width == _subImg2.width)
        {
            if (_subImg.length >= _subImg2.length)
                simage = _subImg2;
            else
                simage = _subImg;
        }
        else if (_subImg.width > _subImg2.width)
            simage = _subImg2;
        else
            simage = _subImg;
        return simage;
    }

    public static bool IsLastBox(Box box, List<Box> _boxes)
    {
        bool retval = true;
        for (int i = 0; i < _boxes.Count(); i++)
        {

            var currbox = _boxes[i];
            if (box.Id == currbox.Id || currbox.position == null)
                continue;

            if (currbox.position.pos_z > box.position.pos_z)
            {
                // Calculate horizontal overlap 
                double x1 = Math.Max(box.position.pos_x, currbox.position.pos_x);
                double x2 = Math.Min(box.position.pos_x + box.width, currbox.position.pos_x + currbox.width);

                // Check if overlap exists
                if (x2 > x1)
                {
                    retval = false; // Found an overlapping box in front
                    break;
                }
            }
        }
        return retval;
    }

    public static bool IsLastImage(Image box, List<Image> _boxes)
    {
        bool retval = true;
        for (int i = 0; i < _boxes.Count(); i++)
        {

            var currbox = _boxes[i];
            if (box.id == currbox.id || currbox.position == null)
                continue;

            if (currbox.position.pos_z > box.position.pos_z && (currbox.position.pos_x <= box.position.pos_x && (currbox.position.pos_x + currbox.width) > box.position.pos_x))
            {
                retval = false;
                break;
            }
        }
        return retval;
    }

    public static List<Image> GetLastImages(List<Image> _lstimages)
    {
        List<Image> retval = new List<Image>();
        foreach (var item in _lstimages)
        {
            if (Helper.IsLastImage(item, _lstimages))
                retval.Add(item);
        }
        return retval;
    }
    public static void ExtendTheBottomArticles_DT(List<ScoreList> _sclist, int pageheight, List<PageInfo> lstFilteredPages)
    {
        //Partial footer is not supported in case of DT, only ads are handled here
        for (int _i = 0; _i < _sclist.Count; _i++)
        {
            ScoreList sc = _sclist[_i];
            PageInfo _pinfo = lstFilteredPages.Where(x => x.sname + x.pageid == sc.pageid).ToList()[0];
            List<Box> _boxes = sc.boxes;
            if (_boxes != null && _boxes.Count > 0)
            {
                for (int _j = 0; _j < _boxes.Count; _j++)
                {
                    Box box = _boxes[_j];
                    bool _adoverlap = false;
                    foreach (PageAds _ads in _pinfo.ads)
                    {
                        if (Math.Max(box.position.pos_x, box.position.pos_x + box.width) > Math.Min(_ads.newx, _ads.newx + _ads.newwidth) &&
                                Math.Min(box.position.pos_x, box.position.pos_x + box.width) < Math.Max(_ads.newx, _ads.newx + _ads.newwidth))
                        {
                            _adoverlap = true;
                            break;
                        }
                    }

                    if (_adoverlap)
                        continue;
                    if (box.position != null)
                    {
                        if (IsLastBox(box, _boxes) && box.articletype == "")
                        {
                            if (box.position.pos_z + box.length < pageheight)
                                box.length = pageheight - box.position.pos_z;
                        }
                    }
                }
            }

        }
    }
    public static void ExtendTheBottomArticles(List<ScoreList> sclist, List<PageInfo> lstFilteredPages)
    {
        for (int _i = 0; _i < sclist.Count; _i++)
        {
            ScoreList sc = sclist[_i];
            PageInfo pageData = lstFilteredPages.Where(x => x.sname + x.pageid == sc.pageid).ToList()[0];
            List<Box> articles = sc.boxes;
            if (pageData != null && articles != null && articles.Count > 0)
            {
                for (int j = 0; j < articles.Count; j++)
                {
                    int pageHeight = CalculateNewPageHeight(pageData);
                    Box article = articles[j];
                    FlowElements lastStaticBox = FlowElements.None;
                    if (article.position != null && IsLastBox(article, articles))
                    {
                        int prevLine = ModelSettings.canvasheight;
                        for (int column = article.position.pos_x; column < article.width + article.position.pos_x; column++)
                        {
                            for (int line = (article.position.pos_z + (int)article.length); line < pageHeight; line++)
                            {
                                if (pageData.paintedCanvas.TryGetValue(new KeyValuePair<int, int>(column, line), out var elementType))
                                {
                                    if (elementType != FlowElements.Editorial)
                                    {
                                        if (prevLine > line)
                                        {
                                            prevLine = line;
                                            lastStaticBox = elementType;
                                        }
                                    }
                                }
                                else
                                {
                                    Log.Warning("Key column}, {line}) not found in paintedCanvas, Extending bottom article failed");
                                    return;
                                }
                            }
                        }
                        if (prevLine != ModelSettings.canvasheight)
                        {
                            pageHeight = prevLine;
                            if (lastStaticBox == FlowElements.Ad)
                            {
                                pageHeight -= ModelSettings.minSpaceBetweenAdsAndStories;
                            }
                            else if (lastStaticBox == FlowElements.Footer)
                            {
                                pageHeight -= ModelSettings.minSpaceBetweenFooterAndStories;
                            }
                        }
                        if (ModelSettings.allowSquareOff && pageHeight - article.squareoffthreshold < article.position.pos_z + article.length)
                        {
                            article.length = pageHeight - article.position.pos_z;
                        }
                        else if (!ModelSettings.allowSquareOff && article.position.pos_z + article.length < pageHeight)
                        {
                            article.length = pageHeight - article.position.pos_z;
                        }
                    }

                }
            }

        }
    }


    public static List<List<Image>> GetPermutationsForSixImages(Dictionary<int, List<Image>> _dictImages, int _width)
    {
        List<List<Image>> result = new List<List<Image>>();
        foreach (var _item in _dictImages[1])
        {

            var _img2list = _dictImages[2].Where(x => x.length == _item.length).ToList();
            if (_img2list == null || _img2list.Count() == 0)
                continue;
            if (_item.width + _img2list[0].width > _width)
                continue;
            List<Image> _tempfirstimagelist = new List<Image>();
            List<Image> _tempsecondimagelist = new List<Image>();
            _tempfirstimagelist.Add(_item);
            _tempsecondimagelist.Add(_img2list[0]);

            foreach (var _item3 in _dictImages[3])
            {
                var _img4list = _dictImages[4].Where(x => x.length == _item3.length).ToList();
                if (_img4list == null || _img4list.Count() == 0)
                    continue;
                if (_item3.width + _img4list[0].width > _width)
                    continue;
                List<Image> _tempthirdimagelist = new List<Image>();
                List<Image> _tempfourthimagelist = new List<Image>();
                _tempthirdimagelist.Add(_item3);
                _tempfourthimagelist.Add(_img4list[0]);

                foreach (var _item5 in _dictImages[5])
                {
                    List<List<Image>> listOfLists = new List<List<Image>>();
                    var _img6list = _dictImages[6].Where(x => x.length == _item5.length).ToList();
                    if (_img6list == null || _img6list.Count() == 0)
                        continue;
                    if (_item5.width + _img6list[0].width > _width)
                        continue;
                    List<Image> _tempfifthimagelist = new List<Image>();
                    List<Image> _tempsixthimagelist = new List<Image>();
                    _tempfifthimagelist.Add(_item5);
                    _tempsixthimagelist.Add(_img6list[0]);

                    listOfLists.Add(_tempfirstimagelist);
                    listOfLists.Add(_tempsecondimagelist);
                    listOfLists.Add(_tempthirdimagelist);
                    listOfLists.Add(_tempfourthimagelist);
                    listOfLists.Add(_tempfifthimagelist);
                    listOfLists.Add(_tempsixthimagelist);

                    var _tresult = Helper.CrossJoin(listOfLists);
                    result.AddRange(_tresult);
                }
            }
        }

        return result;
    }

    public static List<List<Image>> GetPermutationsForFourImages(Dictionary<int, List<Image>> _dictImages, int _width)
    {
        List<List<Image>> result = new List<List<Image>>();
        foreach (var _item in _dictImages[1])
        {

            var _img2list = _dictImages[2].Where(x => x.length == _item.length).ToList();
            if (_img2list == null || _img2list.Count() == 0)
                continue;
            if (_item.width + _img2list[0].width > _width)
                continue;
            List<Image> _tempfirstimagelist = new List<Image>();
            List<Image> _tempsecondimagelist = new List<Image>();
            _tempfirstimagelist.Add(_item);
            _tempsecondimagelist.Add(_img2list[0]);

            foreach (var _item3 in _dictImages[3])
            {
                List<List<Image>> listOfLists = new List<List<Image>>();
                var _img4list = _dictImages[4].Where(x => x.length == _item3.length).ToList();
                if (_img4list == null || _img4list.Count() == 0)
                    continue;
                if (_item3.width + _img4list[0].width > _width)
                    continue;
                List<Image> _tempthirdimagelist = new List<Image>();
                List<Image> _tempfourthimagelist = new List<Image>();
                _tempthirdimagelist.Add(_item3);
                _tempfourthimagelist.Add(_img4list[0]);

                listOfLists.Add(_tempfirstimagelist);
                listOfLists.Add(_tempsecondimagelist);
                listOfLists.Add(_tempthirdimagelist);
                listOfLists.Add(_tempfourthimagelist);


                var _tresult = Helper.CrossJoin(listOfLists);
                result.AddRange(_tresult);
            }
        }

        return result;
    }

    public static void PlaceImagesInsideArticleForDoubleTruck(Box _tbox, List<Image> _images, int _zpos)
    {
        int _length = (int)_tbox.length;
        int _width = (int)_tbox.width - (_tbox.preamble > 0 ? 1 : 0);

        List<Image> _lstfct = null;
        Image _first = null;
        Image _second = null;

        _lstfct = _images.Where(x => x.imagetype == "FactBox").ToList();

        List<Image> _tempImgs = _images.Where(x => x.imagetype == "Image").OrderByDescending(x => -1 * x.length).ThenByDescending(x => x.width).ToList();
        Image _citImage = null;
        if (_images.Where(x => x.imagetype == "Citation").ToList().Count() > 0)
            _citImage = _images.Where(x => x.imagetype == "Citation").ToList()[0];

        if (_tempImgs != null && _tempImgs.Count() > 0)
        {
            _first = _tempImgs[0];
            _second = _tempImgs.Count() > 1 ? _tempImgs[1] : null;
        }
        int _imgwidth = 0;
        if (_second != null)
        {
            Node n = new Node();
            n.pos_x = (int)_tbox.width - _second.width;
            n.pos_z = _zpos;
            _second.position = n;

            Node n1 = new Node();
            n1.pos_x = (int)_tbox.width - _second.width - _first.width;
            n1.pos_z = _zpos;
            _first.position = n1;
            _imgwidth = _second.width + _first.width;
        }
        else if (_first != null)
        {
            Node n1 = new Node();
            n1.pos_x = (int)_tbox.width - _first.width;
            n1.pos_z = _zpos;
            _first.position = n1;
            _imgwidth = _first.width;
        }


        int _startx = _tbox.preamble > 0 ? 2 : 1;
        if (_lstfct != null && _lstfct.Count() > 0)
        {
            int _remwidth = _width - _imgwidth;
            if (_citImage != null)
            {
                _remwidth = _remwidth - _citImage.width - 1;
            }

            int _fctcount = _lstfct.Count();
            int _totfctwidth = 0;
            foreach (var _fct in _lstfct)
                _totfctwidth += _fct.width;
            int _gap = 1;
            if (_totfctwidth + _fctcount < _remwidth)
            {
                foreach (var _fct in _lstfct)
                {
                    Node nf = new Node() { pos_z = _zpos };
                    nf.pos_x = _startx;
                    _fct.position = nf;

                    _totfctwidth = _totfctwidth - _fct.width;
                    _remwidth = _remwidth - _fct.width - _gap;
                    _fctcount--;
                    if (_totfctwidth + _fctcount >= _remwidth)
                        _gap = 0;
                    else
                        _gap = 1;

                    if (_fctcount > 0)
                        _startx = _startx + _fct.width + _gap;
                    else
                        _startx = _startx + _fct.width;
                }
            }
            else
            {
                foreach (var _fct in _lstfct)
                {
                    Node nf = new Node() { pos_z = _zpos };
                    nf.pos_x = _startx;
                    _fct.position = nf;
                    _startx = _startx + _fct.width;
                }
            }
        }

        if (_citImage != null)
        {
            Node n1 = new Node();
            n1.pos_x = _startx + 1;
            n1.pos_z = _zpos;
            _citImage.position = n1;
        }

    }
    public static List<ImageScoreList> GetAllImagePermutationsForDoubleTruck(List<List<Image>> _lstlstFactImages, List<Image> _lstFirstImage, List<Image> _lstSecondImage, List<Image> _lstCitation, int _boxlength)
    {
        try
        {
            if (_boxlength == 23)
                _boxlength = 23;
            if (_lstlstFactImages.Count() > 4)
            {
                _lstCitation = null;
                _lstFirstImage = null;
                _lstSecondImage = null;
            }

            List<ImageScoreList> _lstscores = new List<ImageScoreList>();
            if (_lstlstFactImages != null && _lstlstFactImages.Count() > 0)
            {
                foreach (List<Image> img in _lstlstFactImages)
                {
                    //POK: EPSLN-37: Removing facts where we don't have minimum lines
                    img.RemoveAll(x => x.length > _boxlength - ModelSettings.minimumlinesunderImage);
                }
            }

            //POK: Not setting minimum lines below image restriction on images. They will be expanded to fit the size
            if (_lstFirstImage != null)
            {
                _lstFirstImage.RemoveAll(x => x.length > _boxlength);
                _lstFirstImage.RemoveAll(x => x.length > _boxlength - ModelSettings.minimumlinesunderImage && x.length < _boxlength);
                //for (int _i = 0; _i < _lstFirstImage.Count(); _i++)
                //{
                //    if (_boxlength - _lstFirstImage[_i].length < ModelSettings.minimumlinesunderImage)
                //    {
                //        int _delta = _boxlength - _lstFirstImage[_i].length;
                //        _lstFirstImage[_i].captionlength = _lstFirstImage[_i].captionlength - ModelSettings.extraimagecaptionline;
                //        _lstFirstImage[_i].length += _delta;
                //    }
                //}
            }

            if (_lstSecondImage != null)
            {
                _lstSecondImage.RemoveAll(x => x.length > _boxlength);
                _lstSecondImage.RemoveAll(x => x.length > _boxlength - ModelSettings.minimumlinesunderImage && x.length < _boxlength);
                //for (int _i = 0; _i < _lstSecondImage.Count(); _i++)
                //{
                //    if (_boxlength - _lstSecondImage[_i].length < ModelSettings.minimumlinesunderImage)
                //    {
                //        int _delta = _boxlength - _lstSecondImage[_i].length ;
                //        _lstSecondImage[_i].captionlength = _lstSecondImage[_i].captionlength - ModelSettings.extraimagecaptionline;
                //        _lstSecondImage[_i].length += _delta;
                //    }
                //}
            }

            //POK: EPSLN-37: Removing facts where we don't have minimum lines
            if (_lstCitation != null)
                _lstCitation.RemoveAll(x => x.length > _boxlength - ModelSettings.minimumlinesunderImage);

            if (_lstlstFactImages != null && _lstlstFactImages.Count() > 0)
            {
                int _factarea = 0;
                List<Image> _images = new List<Image>();
                //Only fact
                for (int _i = 0; _i < _lstlstFactImages.Count(); _i++)
                {
                    var _list = _lstlstFactImages[_i].OrderByDescending(x => x.length).ToList();
                    if (_list.Count() > 0)
                    {
                        _factarea += _list[0].width * _list[0].length;
                        _images.Add(_list[0]);
                    }
                }
                //Only fact
                ImageScoreList _t = new ImageScoreList();
                _t.boxes.AddRange(_images);
                _t.totalarea = _factarea;
                _t.imagecount = _images.Count();
                _lstscores.Add(_t);

                //One image - One image or Citation
                _lstscores.AddRange(GetOneImagePermutationForDT(_images, _factarea, _lstFirstImage, _lstSecondImage, _lstCitation));
                //Two image - Two images or 1 Image + Citation
                if (_images.Count() <= 3)
                {
                    _lstscores.AddRange(GetTwoImagePermutationForDT(_images, _factarea, _lstFirstImage, _lstSecondImage, _lstCitation));
                }
                //Three images
                if (_images.Count() <= 2)
                {
                    _lstscores.AddRange(GetThreeImagePermutationForDT(_images, _factarea, _lstFirstImage, _lstSecondImage, _lstCitation));
                }
            }
            else
            {
                //One image - One image or Citation
                _lstscores.AddRange(GetOneImagePermutationForDT(new List<Image>(), 0, _lstFirstImage, _lstSecondImage, _lstCitation));
                //Two image - Two images or 1 Image + Citation
                _lstscores.AddRange(GetTwoImagePermutationForDT(new List<Image>(), 0, _lstFirstImage, _lstSecondImage, _lstCitation));
                //Three images
                _lstscores.AddRange(GetThreeImagePermutationForDT(new List<Image>(), 0, _lstFirstImage, _lstSecondImage, _lstCitation));
            }
            return _lstscores;
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private static List<ImageScoreList> GetOneImagePermutationForDT(List<Image> _fctImages, int _factarea, List<Image> _lstFirstImage, List<Image> _lstSecondImage, List<Image> _lstCitation)
    {
        List<ImageScoreList> _imagescorelist = new List<ImageScoreList>();
        if (_lstFirstImage != null)
        {
            for (int _j = 0; _j < _lstFirstImage.Count(); _j++)
            {
                ImageScoreList _t1 = new ImageScoreList();
                _t1.boxes.AddRange(_fctImages);
                _t1.boxes.Add(_lstFirstImage[_j]);
                _t1.imagecount = _fctImages.Count() + 1;
                _t1.totalarea = _factarea + _lstFirstImage[_j].width * _lstFirstImage[_j].length;
                _imagescorelist.Add(_t1);
            }
        }

        if (_lstCitation != null)
        {
            for (int _j = 0; _j < _lstCitation.Count(); _j++)
            {
                ImageScoreList _t1 = new ImageScoreList();
                _t1.boxes.AddRange(_fctImages);
                _t1.boxes.Add(_lstCitation[_j]);
                _t1.imagecount = _fctImages.Count() + 1;
                _t1.totalarea = _factarea + _lstCitation[_j].width * _lstCitation[_j].length;
                _imagescorelist.Add(_t1);
            }
        }
        return _imagescorelist;
    }

    private static List<ImageScoreList> GetTwoImagePermutationForDT(List<Image> _fctImages, int _factarea, List<Image> _lstFirstImage, List<Image> _lstSecondImage, List<Image> _lstCitation)
    {
        List<ImageScoreList> _imagescorelist = new List<ImageScoreList>();
        //minimum two images are required
        if (_lstFirstImage == null)
            return _imagescorelist;
        else if (_lstSecondImage == null && _lstCitation == null)
            return _imagescorelist;

        if (_lstSecondImage != null)
        {
            for (int _j = 0; _j < _lstFirstImage.Count(); _j++)
            {
                for (int _k = 0; _k < _lstSecondImage.Count(); _k++)
                {
                    ImageScoreList _t1 = new ImageScoreList();
                    _t1.boxes.AddRange(_fctImages);
                    _t1.boxes.Add(_lstFirstImage[_j]);
                    _t1.boxes.Add(_lstSecondImage[_k]);
                    _t1.imagecount = _fctImages.Count() + 2;
                    _t1.totalarea = _factarea + _lstFirstImage[_j].width * _lstFirstImage[_j].length + _lstSecondImage[_k].width * _lstSecondImage[_k].length;
                    _imagescorelist.Add(_t1);
                }
            }
        }
        if (_lstCitation != null)
        {
            for (int _j = 0; _j < _lstFirstImage.Count(); _j++)
            {
                for (int _k = 0; _k < _lstCitation.Count(); _k++)
                {
                    ImageScoreList _t1 = new ImageScoreList();
                    _t1.boxes.AddRange(_fctImages);
                    _t1.boxes.Add(_lstFirstImage[_j]);
                    _t1.boxes.Add(_lstCitation[_k]);
                    _t1.imagecount = _fctImages.Count() + 2;
                    _t1.totalarea = _factarea + _lstFirstImage[_j].width * _lstFirstImage[_j].length + _lstCitation[_k].width * _lstCitation[_k].length;
                    _imagescorelist.Add(_t1);
                }
            }
        }
        return _imagescorelist;
    }


    private static List<ImageScoreList> GetThreeImagePermutationForDT(List<Image> _fctImages, int _factarea, List<Image> _lstFirstImage, List<Image> _lstSecondImage, List<Image> _lstCitation)
    {
        List<ImageScoreList> _imagescorelist = new List<ImageScoreList>();
        //all three images are required
        if (_lstFirstImage == null || _lstSecondImage == null || _lstCitation == null)
            return _imagescorelist;


        for (int _j = 0; _j < _lstFirstImage.Count(); _j++)
        {
            for (int _k = 0; _k < _lstSecondImage.Count(); _k++)
            {
                for (int _l = 0; _l < _lstCitation.Count(); _l++)
                {
                    ImageScoreList _t1 = new ImageScoreList();
                    _t1.boxes.AddRange(_fctImages);
                    _t1.boxes.Add(_lstFirstImage[_j]);
                    _t1.boxes.Add(_lstSecondImage[_k]);
                    _t1.boxes.Add(_lstCitation[_l]);
                    _t1.imagecount = _fctImages.Count() + 3;
                    _t1.totalarea = _factarea + _lstFirstImage[_j].width * _lstFirstImage[_j].length + _lstSecondImage[_k].width * _lstSecondImage[_k].length + _lstCitation[_l].width * _lstCitation[_l].length;
                    _imagescorelist.Add(_t1);
                }
            }
        }

        return _imagescorelist;
    }

    public static void MoveLargestFactsToRight(List<Image> _fctList, int _length)
    {
        var tempfctList = _fctList.OrderBy(x => x.length).ToList();

        // Re-assign the fact list 
        _fctList.Clear();
        _fctList.AddRange(tempfctList);
        //int _i = 0;
        //List<Image> _temp = new List<Image>();
        //for (_i = 0; _i < _fctList.Count(); _i++)
        //{
        //    Image _ti = _fctList[_i];
        //    if (_ti.length == _length)
        //    {
        //        _fctList.RemoveAt(_i);
        //        _temp.Add(_ti);
        //        _i--;
        //    }
        //}
        //foreach (Image _tti in _temp)
        //    _fctList.Add(_tti);
    }

    public static void RemoveDuplicateArticleSizes(List<Box> _boxlist)
    {
        int _width = 0, _height = 0;
        for (int _i = 0; _i <= _boxlist.Count() - 1; _i++)
        {
            Box _item = _boxlist[_i];
            if (_item.width == _width && _item.length == _height)
            {
                _width = (int)_item.width;
                _height = (int)_item.length;
                _boxlist.RemoveAt(_i);
                _i--;
            }
            else
            {
                _width = (int)_item.width;
                _height = (int)_item.length;
            }
        }
    }

    public static void RemoveDuplicateArticleSizesUsingScore(List<Box> _boxlist, Box _box, PageInfo _pinfo)
    {
        Log.Information("Inside RemoveDuplicateArticleSizesUsingScore for article: {id}", _box.Id);
        if (_box.imageList.Count < ModelSettings.rewards.layoutpreference.minimumimagesinlayout)
        {
            RemoveDuplicateArticleSizes(_boxlist);
            return;
        }

        int _width = 0, _height = 0;
        int _prevscore = 0;
        int _currscore = 0;
        int _previmgcount = 0, _currimgcount = 0;
        if (_box.Id == "224a69d8-5042-43f2-aaeb-b4f0744bf8ef")
            _width = 0;
            //_boxlist.DistinctBy(p => new { p.width, p.length })
        for (int _i = 0; _i <= _boxlist.Count() - 1; _i++)
        {
            Box _item = _boxlist[_i];
            if (_item.layouttype == LayoutType.aboveheadline)
            {
                Box _test = Helper.CustomCloneBox(_item);
                Box _tes2 = Helper.DeepCloneBox(_item);
            }
            _item.position = new Node();
            BigInteger[] _retval = calculateScore(new List<Box>() { _item }, true, _pinfo);
            _currscore = (int)_retval[1];
            _currimgcount = _item.usedimagecount;
            if (_item.width == _width && _item.length == _height)
            {
                if (_currimgcount < _previmgcount)
                    _boxlist.RemoveAt(_i);
                else if (_currscore > _prevscore || _currimgcount>_previmgcount)
                {
                    _boxlist.RemoveAt(_i - 1);
                    _prevscore = _currscore;
                    _previmgcount = _currimgcount;
                }
                else
                    _boxlist.RemoveAt(_i);
                _i--;
            }
            else
            {
                _width = (int)_item.width;
                _height = (int)_item.length;
                _prevscore = _currscore;
                _previmgcount = _currimgcount;
            }
        }

        _boxlist.ForEach(x => x.position = null);
    }

    public static Node CustomCloneNode(Node _node)
    {
        if (_node == null)
            return null;
        Node _tempn = new Node
        {
            //POK: Wedon't need to clone these right/bottom nodes. Packer  will assign it
            //rightNode = _node.rightNode,
            //bottomNode = _node.bottomNode,
            pos_x = _node.pos_x,
            pos_z = _node.pos_z,
            width = _node.width,
            length = _node.length,
            isOccupied = _node.isOccupied
        }
        ;
        return _tempn;
    }

    public static SizeD CustomCloneSizeD(SizeD mshot)
    {
        if (mshot == null)
            return null;

        SizeD obj = new SizeD(mshot.Width, mshot.Length, mshot.CaptionLength);
        return obj;
    }

    public static ImageMetadata CustomCloneImageMetaData(ImageMetadata _meta)
    {
        if (_meta == null)
            return null;
        ImageMetadata _tempn = new ImageMetadata(_meta.isMugshot, _meta.isGraphic, _meta.allowCrop, _meta.height, _meta.sizes, CustomCloneSizeD(_meta.doubleSize));

        return _tempn;
    }
    public static List<Image> CustomCloneListImage(List<Image> _images)
    {
        if (_images == null)
            return null;
        List<Image> _outputlist = new List<Image>();

        foreach (Image _image in _images)
        {
            _outputlist.Add(CustomCloneImage(_image));
        }

        return _outputlist;
    }

    public static List<Box> CustomCloneListBox(List<Box> _articles)
    {
        if (_articles == null)
            return null;
        List<Box> _outputlist = new List<Box>();

        foreach (Box _article in _articles)
        {
            _outputlist.Add(CustomCloneBox(_article));
        }

        return _outputlist;
    }
    public static Box CloneArticleWithoutMugshots(Box original)
    {
        var cloned = DeepCloneBox(original);
        cloned.imageList.RemoveAll(img => img.imagetype == "mugshot");
        return cloned;
    }
    //POK: THIS FUNCTION SHOULD ONLY BE USED BY PACKER. AS IT DOESN'T COPY POSTION OBJECT
    public static PageInfo CustomClonePageInfo(PageInfo _pinfo)
    {
        if (_pinfo == null) return null;

        PageInfo _tempi = new PageInfo
        {
            pageid = _pinfo.pageid,
            ads = CustomCloneListPageAds(_pinfo.ads),
            staticBoxes = CustomCloneListStaticBox(_pinfo.staticBoxes)
        }
        ;
        return _tempi;
    }

    public static StaticBox CustomCloneStaticBox(int startx, int starty, int boxwidth, int boxheight, FlowElements type)
    {
        return new StaticBox(startx, starty, boxwidth, boxheight, type);
    }
    public static StaticBox CustomCloneStaticBox(StaticBox _staticbox)
    {
        if (_staticbox == null) return null;

        StaticBox _tempi = new StaticBox(_staticbox.X, _staticbox.Y, _staticbox.Width, _staticbox.Height, _staticbox.Type);

        return _tempi;
    }
    public static PageAds CustomClonePageAds(PageAds _ad)
    {
        if (_ad == null) return null;

        PageAds _tempi = new PageAds
        {
            newx = _ad.newx,
            newy = _ad.newy,
            newwidth = _ad.newwidth,
            newheight = _ad.newheight
        }
        ;
        return _tempi;
    }

    public static List<StaticBox> CustomCloneListStaticBox(List<StaticBox> _staticboxes)
    {
        if (_staticboxes == null) return null;

        List<StaticBox> _outputlist = new List<StaticBox>();

        foreach (StaticBox _sb in _staticboxes)
        {
            _outputlist.Add(CustomCloneStaticBox(_sb));
        }

        return _outputlist;
    }
    public static List<PageAds> CustomCloneListPageAds(List<PageAds> _ads)
    {
        if (_ads == null) return null;

        List<PageAds> _outputlist = new List<PageAds>();

        foreach (PageAds _ad in _ads)
        {
            _outputlist.Add(CustomClonePageAds(_ad));
        }

        return _outputlist;
    }
    //POK: THIS FUNCTION SHOULD ONLY BE USED BY PACKER. AS IT DOESN'T COPY POSTION OBJECT
    public static Image CustomCloneImage(Image _image)
    {
        if (_image == null) return null;

        Image _tempi = new Image
        {
            origlength = _image.origlength,
            origwidth = _image.origwidth,
            priority = _image.priority,
            id = _image.id,
            length = _image.length,
            width = _image.width,
            captionlength = _image.captionlength,
            cropped = _image.cropped,
            imagetype = _image.imagetype,
            aboveHeadline = _image.aboveHeadline,
            mainImage = _image.mainImage,
            topimageinsidearticle = _image.topimageinsidearticle,
            parentimageid = _image.parentimageid,
            imageorderId = _image.imageorderId,
            croppercentage = _image.croppercentage,
            relativex = _image.relativex,
            relativey = _image.relativey,
            islandscape = _image.islandscape,
            //area = _image.area,
            position = CustomCloneNode(_image.position), //POK: Not needed. Packer will assign it
            fixedWidthImage = _image.fixedWidthImage,
            imageMetadata = CustomCloneImageMetaData(_image.imageMetadata),
            isHeadlineHeightIncluded = _image.isHeadlineHeightIncluded,
        };
        return _tempi;
    }

    //POK: THIS FUNCTION SHOULD ONLY BE USED BY PACKER. AS IT DOESN'T COPY POSTION OBJECT
    public static Box CustomCloneBox(Box _box)
    {
        Box _tempb = new Box
        {
            Id = _box.Id,
            origArea = _box.origArea,
            priority = _box.priority,
            length = _box.length,
            width = _box.width,
            rank = _box.rank,
            usedaboveimagecount = _box.usedaboveimagecount,
            boxorderId = _box.boxorderId,
            runid = _box.runid,
            headlinecaption = _box.headlinecaption,
            headlinelength = _box.headlinelength,
            kickerlength = _box.kickerlength,
            usedimagecount = _box.usedimagecount,
            preamble = _box.preamble,
            byline = _box.byline,
            origminArea = _box.origminArea,
            minApproxArea = _box.minApproxArea,
            volume = _box.volume,
            category = _box.category,
            headlinewidth = _box.headlinewidth,
            page = _box.page,
            bodywordcount = _box.bodywordcount,
            UniqueId = _box.UniqueId,
            isMandatory = _box.isMandatory,
            isdoubletruck = _box.isdoubletruck,
            parentArticleId = _box.parentArticleId,
            whitespace = _box.whitespace,
            position = CustomCloneNode(_box.position), //POK: Not needed. Packer will assign it
            articletype = _box.articletype,
            isNewPage = _box.isNewPage,
            layout = _box.layout,

            isjumparticle = _box.isjumparticle,
            jumpTowidth = _box.jumpTowidth,
            jumpTolength = _box.jumpTolength,
            jumpFromwidth = _box.jumpFromwidth,
            jumpFromlength = _box.jumpFromlength,
            jumpfrompageid = _box.jumpfrompageid,
            jumptopageid = _box.jumptopageid,
            pagesname = _box.pagesname,
            allowoverset = _box.allowoverset,
            oversetarea = _box.oversetarea,
            squareoffthreshold = _box.squareoffthreshold,
            placementposition = _box.placementposition,
            headlinePosition = _box.headlinePosition,
            kickerPosition = _box.kickerPosition,
            bylinePosition = _box.bylinePosition,
            preamblePosition = _box.preamblePosition,
            jumpSection = _box.jumpSection,
            layouttype = _box.layouttype,
            multiFactStackingOrder = _box.multiFactStackingOrder,
            headlinetypoline = _box.headlinetypoline
        };
        if (_box.usedImageList != null)
        {
            _tempb.usedImageList = new List<Image>();
            foreach (var _item in _box.usedImageList)
                _tempb.usedImageList.Add(CustomCloneImage(_item));
        }


        //Box _tempb = Helper.DeepCloneBox(_box);
        return _tempb;
    }

    public static void AddExtraLineBetweenHeadlineAndImages(Box _box)
    {
        Image _mainImage = null;
        if (_box.usedImageList != null && _box.usedImageList.Count(x => x.aboveHeadline == true) > 0)
        {
            List<Image> _lstImages = _box.usedImageList.Where(x => x.aboveHeadline == true).ToList();
            _mainImage = _lstImages.First(x => x.mainImage == 1);
            if (_mainImage.width == _box.width) //Images could be below main
            {
                if (_lstImages.Count == 1)
                {
                    _mainImage.captionlength = _mainImage.captionlength + 1;
                }
                else
                {
                    foreach (Image _img in _lstImages.Where(x => x.mainImage == 0))
                        _img.captionlength = _img.captionlength + 1;
                }
            }
            else //Images could be next to main
            {
                _mainImage.captionlength = _mainImage.captionlength + 1;
                Image _img = _box.usedImageList.Last(x => x.aboveHeadline == true);
                if (_img.imagetype.ToUpper() == "IMAGE")
                    _img.captionlength = _img.captionlength + 1;
            }

        }
    }
    public static void AddExtraLineBetweenHeadlineAndImages(List<ScoreList> _sclist)
    {
        for (int _i = 0; _i < _sclist.Count; _i++)
        {
            ScoreList sc = _sclist[_i];

            List<Box> _boxes = sc.boxes;
            if (_boxes != null && _boxes.Count > 0)
            {
                foreach (Box _box in _boxes.Where(x => x.position != null))
                {
                    AddExtraLineBetweenHeadlineAndImages(_box);
                }
            }

        }
    }

    public static void AddExtraLineBetweenHeadlineAndImages(List<Image> boveHeadlineImages)
    {
        // This should be called with above headline images only.
        if (ModelSettings.extralineaboveHeadline > 0)
        {
            if (boveHeadlineImages.Any(x => x.aboveHeadline == false))
            {
                Log.Warning("AddExtraLineBetweenHeadlineAndImages: It has below headline images ");
                return;
            }


            var lastImages = GetLastImages(boveHeadlineImages);
            foreach (var img in lastImages)
            {
                img.captionlength += 1;
            }
        }
    }

    public static bool AreIntersectedWidthStaticBox(Box r1, StaticBox r2)
    {
        double x1 = Math.Max(r1.pos_x, r2.X);
        double x2 = Math.Min(r1.pos_x + r1.width, r2.X + r2.Width);
        //To keep minimum distance between Ads and stories
        double y1 = Math.Max(r1.pos_z, r2.Y - ModelSettings.minSpaceBetweenAdsAndStories);
        double y2 = Math.Min(r1.pos_z + r1.length, r2.Y + r2.Height);

        return (x2 > x1 && y2 > y1);
    }
    public static bool AreIntersected(Box r1, PageAds r2)
    {
        double x1 = Math.Max(r1.pos_x, r2.newx);
        double x2 = Math.Min(r1.pos_x + r1.width, r2.newx + r2.newwidth);
        //To keep minimum distance between Ads and stories
        double y1 = Math.Max(r1.pos_z, r2.newy - ModelSettings.minSpaceBetweenAdsAndStories);
        double y2 = Math.Min(r1.pos_z + r1.length, r2.newy + r2.newheight);

        return (x2 > x1 && y2 > y1);
    }

    public static bool AreIntersected(Rectangle r1, PageAds r2)
    {
        double x1 = Math.Max(r1.x, r2.newx);
        double x2 = Math.Min(r1.x + r1.width, r2.newx + r2.newwidth);
        double y1 = Math.Max(r1.y, r2.newy);
        double y2 = Math.Min(r1.y + r1.height, r2.newy + r2.newheight);
        return (x2 > x1 && y2 > y1);


    }

    public static Image SetImagePosition(Image image, int posX, int posZ)
    {
        image.position = CreateNode(posX, posZ, image.width, image.length);
        return image;
    }

    public static bool IsOverlap(int start1, double end1, int start2, int end2)
    {
        return Math.Max(start1, end1) > Math.Min(start2, end2) && Math.Min(start1, end1) < Math.Max(start2, end2);
    }
    public static bool IsBoxOverlappingWithStaticBox(Box box, List<StaticBox> sboxes)
    {
        foreach (var staticBox in sboxes)
        {
            if (staticBox.Type != FlowElements.Header &&
                IsOverlap(box.position.pos_x, box.position.pos_x + box.width, staticBox.X, staticBox.X + staticBox.Width))
            {
                return true;
            }
        }
        return false;
    }
    public static bool IsBoxOverlappingWithStaticBox(int start1, double end1, List<StaticBox> sBoxes)
    {
        foreach (var staticBox in sBoxes)
        {
            if (staticBox.Type != FlowElements.Header &&
                IsOverlap(start1, end1, staticBox.X, staticBox.X + staticBox.Width))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsFooterOnTopHalf(PageInfo page)
    {
        //no null check, call this must be bounded with null check
        return page.footer != null && page.footer.y < ModelSettings.canvasheight / 2;
    }

    public static int CalculateNewPageHeight(PageInfo pageInfo)
    {
        if (pageInfo.pageEditorialArea != null)
        {
            return pageInfo.pageEditorialArea.Y + pageInfo.pageEditorialArea.Height;
        }
        return ModelSettings.canvasheight;
    }

    public static Filler GetBestFillerForWhiteSpace(int x, double y, double h, double w, string pageCateogry, int pageId, Dictionary<KeyValuePair<double, double>, Filler> allInputFillers)
    {
        Log.Information("Found White space of h = {h}, w = {w}, x = {x}, y = {y} on the page {id}", h, w, x, y, pageId);

        double newy = 0;
        string alignment = "bottom";
        if (ModelSettings.fillerAlignment.TryGetValue("top", out var sections) && sections.Contains(pageCateogry))
        {
            alignment = "top";
        }
        int whiteSpaceHeight = (int)h;
        int boxWidth = (int)w;
        var suitableFiller = allInputFillers.Where(filler => filler.Key.Key == boxWidth && !filler.Value.canNotUsedInSection.Contains(pageCateogry));
        if (suitableFiller.Count() > 0)
        {
            var nearestLineFiller = allInputFillers.Where(filler => filler.Key.Key == boxWidth && Math.Round(filler.Key.Value) <= whiteSpaceHeight).OrderByDescending(filler => filler.Key.Value)
                .FirstOrDefault();
            if (nearestLineFiller.Value != null)
            {
                if (allInputFillers.TryGetValue(nearestLineFiller.Key, out var fillerObj))
                {
                    Filler filler = new Filler();
                    double newx = x;
                    filler.Width = w;
                    filler.original_Width = (ModelSettings.columnWidth * w + ModelSettings.gutterWidth * (w - 1)) / ModelSettings.columnWidth;
                    filler.Height = nearestLineFiller.Key.Value;
                    filler.original_Height = nearestLineFiller.Key.Value;
                    switch (alignment)
                    {
                        case "top":
                            newy = y;
                            break;

                        case "bottom":
                            newy = y + h - nearestLineFiller.Key.Value;
                            break;
                        case "center":
                            newy = y + (h - nearestLineFiller.Key.Value) / 2;
                            break;
                        default: break;
                    }
                    filler.x = newx;
                    filler.y = newy;
                    filler.Width = w;
                    filler.original_x = newx == 0 ? 0 : (ModelSettings.columnWidth * newx + ModelSettings.gutterWidth * newx) / ModelSettings.columnWidth;
                    filler.original_y = filler.y;
                    filler.file = fillerObj.file;
                    filler.pageId = pageId;
                    return filler;
                }
            }
            else
            {
                Log.Information("Info : No nearest filler found of space of width {w} and height {h}", w, whiteSpaceHeight);
            }
        }
        else
        {
            Log.Information("Info : No sutable filler found of space of width {w}", w);
        }
        return null;
    }

    public static void PaintUsedArea(Box box, Dictionary<KeyValuePair<int, int>, bool> pageCoordinates)
    {
        int x = box.position.pos_x;
        int y = box.position.pos_z;
        int h = (int)box.length + ModelSettings.minSpaceBetweenAdsAndStories;
        int w = (int)box.width;
        for (int i = y; i < y + h; i++)
        {
            for (int j = x; j < x + w; j++)
            {
                pageCoordinates[new KeyValuePair<int, int>(j, i)] = true;
            }
        }

    }

    public static Dictionary<KeyValuePair<int, int>, bool> InitializePageCoordinates(PageInfo pageInfo)
    {
        var pageCoordinates = new Dictionary<KeyValuePair<int, int>, bool>();

        for (int i = 1; i <= ModelSettings.canvasheight; i++)
        {
            for (int j = 0; j < ModelSettings.canvaswidth; j++)
            {
                pageCoordinates[new KeyValuePair<int, int>(j, i)] = false;
            }
        }
        //Headline
        for (int i = 1; i <= pageInfo.sectionheaderheight; i++)
        {
            for (int j = 0; j < ModelSettings.canvaswidth; j++)
            {
                pageCoordinates[new KeyValuePair<int, int>(j, i)] = true;
            }
        }
        //footer
        if (pageInfo.footer != null)
        {
            for (int i = pageInfo.footer.y; i <= pageInfo.footer.y + pageInfo.footer.height; i++)
            {
                for (int j = pageInfo.footer.x; j < pageInfo.footer.x + pageInfo.footer.width; j++)
                {
                    pageCoordinates[new KeyValuePair<int, int>(j, i)] = true;
                }
            }
        }


        //Anything else to be marked as used? Ask Nikhil
        if (ModelSettings.bAllowPlacingFillerAboveTheAd)
        {
            foreach (var ad in pageInfo.ads)
            {
                int adRight = ad.newx + ad.newwidth;
                int adBottom = ad.newy + ad.newheight;

                for (int i = ad.newy - ModelSettings.minimumSpaceInAdAndFiller; i < adBottom; i++)
                {
                    for (int j = ad.newx; j < adRight; j++)
                    {
                        pageCoordinates[new KeyValuePair<int, int>(j, i)] = true;
                    }
                }
            }
        }
        return pageCoordinates;
    }

    public static Node CreateNode(int posX, int posZ, int width, int length) => new Node
    {
        pos_x = posX,
        pos_z = posZ,
        width = width,
        length = length
    };

    public static bool IsAdOnBottom(PageInfo pInfo)
    {
        return pInfo.staticBoxes.Any(x => x.Type == FlowElements.Ad && pInfo.pageEditorialArea.Y + pInfo.pageEditorialArea.Height == x.Y);
    }

    public static bool IsFooterOnBottom(PageInfo pInfo)
    {
        return pInfo.staticBoxes.Any(x => x.Type == FlowElements.Footer && pInfo.pageEditorialArea.Y + pInfo.pageEditorialArea.Height == x.Y);
    }

    public static StaticBox GetMultispreadSpace(PageInfo page)
    {
        return GetEditorialSpace(page);
    }

    public static StaticBox GetMultispreadSpace(PageInfo page1, PageInfo page2)
    {
        var spacePage1 = GetEditorialSpace(page1);
        var spacePage2 = GetEditorialSpace(page2);

        return new StaticBox
        {
            Type = FlowElements.Editorial,
            Height = Math.Min(spacePage1.Height, spacePage2.Height),
            Width = spacePage1.Width + spacePage2.Width,
            X = spacePage1.X,
            Y = spacePage1.Y,
        };
    }

    /// <summary>
    /// This will be used to get editorial space considering the longes depth as in height and width
    /// </summary>
    /// <param name="pInfo"> page informations </param>
    /// <returns>A static box (x,y,w,h)of type editorail</returns>
    public static StaticBox GetEditorialSpace(PageInfo pInfo)
    {
        int startY = ModelSettings.canvasheight;
        for (int column = 0; column < ModelSettings.canvaswidth; column++)
        {
            int newStartY = GetStartFromLine(column, pInfo.paintedCanvas);
            startY = Math.Min(startY, newStartY);
        }
        int startX = -1;
        int editorialWidth = 0;
        for (int column = 0; column < ModelSettings.canvaswidth; column++)
        {
            if (HasEditoriaSpaceIncludingAllowedSpacing(pInfo, startY, column))
            {
                if (startX == -1)
                    startX = column;

                editorialWidth++;
            }
        }
        if (startX == -1 || startY == ModelSettings.canvasheight)
            return null;
        return new StaticBox(startX, startY, editorialWidth, ModelSettings.canvasheight - startY, FlowElements.Editorial);
    }
    private static bool HasEditoriaSpaceIncludingAllowedSpacing(PageInfo pInfo, int y, int x)
    {
        for (int spacing = y; spacing <= y + ModelSettings.allowedStaticBoxSpacingY; spacing++)
        {
            if (pInfo.paintedCanvas.TryGetValue(new KeyValuePair<int, int>(x, spacing), out var e))
            {
                if (e != FlowElements.Editorial)
                {
                    return false;
                }
            }
            else
            {
                Log.Warning("Key not found in dictionary = " + x + " , " + spacing + " root cause -> allowedStaticBoxSpacingY may configured wrong");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Used in picture story only, and supported full width static boxes/ furnitures 
    /// </summary>
    /// <param name="staticBoxes">List of static boxes (all ads, header and footers)</param>
    /// <returns>A static box (x,y,w,h)of type editorail</returns>
    public static StaticBox GetEditorialSpace_PictureStory(List<StaticBox> staticBoxes, bool isDT = false)
    {
        //Static elements are considred as full width and full height only in picture story (may be will be supporting the partial in future)
        int pageWidth = ModelSettings.canvaswidth * (isDT ? 2 : 1);
        int pageHeight = ModelSettings.canvasheight;
        int startingX = 0, startingY = 0;

        List<StaticBox> availableAreas = new List<StaticBox>
        {
            new StaticBox(startingX, startingY, pageWidth, pageHeight, FlowElements.Editorial)
        };

        foreach (var staticBox in staticBoxes)
        {
            List<StaticBox> newAvailableAreas = new List<StaticBox>();
            foreach (var area in availableAreas)
            {
                if (area.IsOverlapping(staticBox))
                {
                    if (staticBox.X > area.X)
                        newAvailableAreas.Add(new StaticBox(area.X, area.Y, staticBox.X - area.X, area.Height, FlowElements.None));

                    if (staticBox.X + staticBox.Width < area.X + area.Width)
                        newAvailableAreas.Add(new StaticBox(staticBox.X + staticBox.Width, area.Y, (area.X + area.Width) - (staticBox.X + staticBox.Width), area.Height, FlowElements.None));

                    if (staticBox.Y > area.Y)
                        newAvailableAreas.Add(new StaticBox(area.X, area.Y, area.Width, staticBox.Y - area.Y, FlowElements.None));

                    if (staticBox.Y + staticBox.Height < area.Y + area.Height)
                        newAvailableAreas.Add(new StaticBox(area.X, staticBox.Y + staticBox.Height, area.Width, (area.Y + area.Height) - (staticBox.Y + staticBox.Height), FlowElements.None));
                }
                else newAvailableAreas.Add(area);
            }
            availableAreas = newAvailableAreas;
        }
        return availableAreas.OrderByDescending(x => x.Height * x.Width).FirstOrDefault();
    }

    public static void AdjustSpacingBetweenElements(PageInfo pageInfo, StaticBox editorialSpace)
    {
        if (editorialSpace != null && pageInfo != null)
        {
            var footer = pageInfo.footer;
            if (footer != null && footer.y < ModelSettings.canvasheight / 2)
            {
                //Check if footer ending just before story starting
                if (footer.y + footer.height == editorialSpace.Y)
                {
                    editorialSpace.Y = editorialSpace.Y + ModelSettings.minSpaceBetweenFooterAndStories;
                    editorialSpace.Height -= ModelSettings.minSpaceBetweenFooterAndStories;
                }
            }
            if (footer != null && footer.y >= ModelSettings.canvasheight / 2)
            {
                if (editorialSpace.Y + editorialSpace.Height <= footer.y)
                {
                    editorialSpace.Height -= ModelSettings.minSpaceBetweenFooterAndStories; // Footer on bottom
                }
            }

            if (pageInfo.ads.Any(x => x.newy + x.newheight == editorialSpace.Y))
            {
                editorialSpace.Y = editorialSpace.Y + ModelSettings.minSpaceBetweenAdsAndStories;
                editorialSpace.Height -= ModelSettings.minSpaceBetweenAdsAndStories;
            }
            else if (pageInfo.ads.Any(x => x.newy + x.newheight < editorialSpace.Y))
            {
                //editorials y overlapping with ads, Should not happen this, lets fix this here
                Log.Error("Packing area is overlapping with one of the ads");
            }
        }
    }
    public static int GetStartFromLine(int column, Dictionary<KeyValuePair<int, int>, FlowElements> canvas)
    {
        for (int y = 0; y < ModelSettings.canvasheight; y++)
        {
            if (canvas.TryGetValue(new KeyValuePair<int, int>(column, y), out var element) && element == FlowElements.Editorial)
            {
                //Lets check if some space exists
                bool bIsStaticBoxFound = false;
                for (int newY = y; newY <= (ModelSettings.allowedStaticBoxSpacingY + y); newY++)
                {
                    if (canvas.TryGetValue(new KeyValuePair<int, int>(column, newY), out var element1) && element1 != FlowElements.Editorial)
                    {
                        bIsStaticBoxFound = true;
                        y = newY;
                        break;
                    }
                }
                if (bIsStaticBoxFound)
                {
                    continue;
                }
                return y;
            }
        }
        return ModelSettings.canvasheight;
    }

    public static int GetDepth(int col, int startY, Dictionary<KeyValuePair<int, int>, FlowElements> canvas)
    {
        int depth = 0;
        if (startY < ModelSettings.canvasheight && canvas[new KeyValuePair<int, int>(col, startY)] == FlowElements.Editorial)
        {
            for (int y = startY; y < ModelSettings.canvasheight; y++)
            {
                if (canvas[new KeyValuePair<int, int>(col, y)] == FlowElements.Editorial)
                    depth++;
                else break;
            }
        }
        return depth;
    }
    public static Dictionary<KeyValuePair<int, int>, FlowElements> GetPainedCanvas(List<StaticBox> staticBoxes)
    {
        Dictionary<KeyValuePair<int, int>, FlowElements> canvas = new Dictionary<KeyValuePair<int, int>, FlowElements>();
        for (int y = 0; y < ModelSettings.canvasheight; y++)
        {
            for (int x = 0; x < ModelSettings.canvaswidth; x++)
            {
                canvas[new KeyValuePair<int, int>(x, y)] = FlowElements.Editorial;
            }
        }
        foreach (var box in staticBoxes)
        {
            for (int y = box.Y; y < box.Y + box.Height; y++)
            {
                for (int x = box.X; x < box.X + box.Width; x++)
                {
                    canvas[new KeyValuePair<int, int>(x, y)] = box.Type;
                }
            }
        }
        return canvas;
    }

    /// <summary>
    /// determine the most suitable packer to fit the article on the page by comparing the left and right depths 
    /// (from top to bottom, considering any static box). If the left depth is greater, the left packer will be invoked; otherwise,
    /// the right packer will be used. And if enablePlacementFromTopRight = false, the left packer will always be used.
    /// </summary>
    /// <param name="page">Contains page informations</param>
    /// <returns>Enum right/left</returns>
    public static FlowPacker GetBestPacker(PageInfo page)
    {
        if (!ModelSettings.enablePlacementFromTopRight)
            return FlowPacker.TopLeft;

        int leftDepth = 0;
        for (int coulmn = 0; coulmn < ModelSettings.canvaswidth; coulmn++)
        {
            int startFromY = GetStartFromLine(coulmn, page.paintedCanvas); // This will retun value other than canvasheight if not a full height editorial space at column = x
            if (startFromY == ModelSettings.canvasheight)
            {
                continue; // Full width, lets check next coulmn
            }
            leftDepth = GetDepth(coulmn, startFromY, page.paintedCanvas);
            if (leftDepth != 0)
            {
                break;
            }
        }

        int rightDepth = 0;
        for (int coulmn = ModelSettings.canvaswidth - 1; coulmn >= 0; coulmn--)
        {
            int startFromY = GetStartFromLine(coulmn, page.paintedCanvas);
            if (startFromY == ModelSettings.canvasheight)
            {
                continue;// Full hieght, lets check next coulmn
            }
            rightDepth = GetDepth(coulmn, startFromY, page.paintedCanvas);
            if (rightDepth != 0)
            {
                break;
            }
        }
        return (rightDepth > leftDepth) ? FlowPacker.TopRight : FlowPacker.TopLeft;
    }

    public static int GetNumericalPriority(string _priority)
    {
        int _ipriority = 1;

        if (_priority.Equals("A", StringComparison.OrdinalIgnoreCase))
            _ipriority = 5;
        if (_priority.Equals("B", StringComparison.OrdinalIgnoreCase))
            _ipriority = 4;
        if (_priority.Equals("C", StringComparison.OrdinalIgnoreCase))
            _ipriority = 3;
        if (_priority.Equals("D", StringComparison.OrdinalIgnoreCase))
            _ipriority = 2;
        if (_priority == "E")
            _ipriority = 1;

        return _ipriority;
    }

    //This function returns the json for the image objects
    public static AutomationPageArticleItems GetJumpImageItem(Image _mainImg, string boxid)
    {
        AutomationPageArticleItems imgitem = new AutomationPageArticleItems();
        imgitem.x = _mainImg.position.pos_x;
        imgitem.y = _mainImg.position.pos_z;
        imgitem.width = _mainImg.width;
        imgitem.height = _mainImg.length;
        AutomationImageCaption _imgcaption = new AutomationImageCaption()
        {
            type = "caption",
            height = _mainImg.captionPosition.length,
            width = _mainImg.captionPosition.width,
            x = _mainImg.captionPosition.pos_x,
            y = _mainImg.captionPosition.pos_z
        };

        imgitem.caption = _imgcaption;
        imgitem.image_id = _mainImg.id;
        imgitem.type = "image";
        if (_mainImg.imagetype != "Image")
            imgitem.factbox = true;
        else
            imgitem.factbox = false;

        imgitem.cropped = _mainImg.cropped;
        imgitem.article_id = boxid;
        if (_mainImg.imagetype == "Image")
            imgitem.original_aspect_ratio = (double)_mainImg.origwidth / (double)_mainImg.origwidth;
        return imgitem;
    }
    public static (int rect1Images, int rect2Images) DistributeImagesIn2Rects(Rectangle rect1, Rectangle rect2, List<Image> images)
    {
        var rect1Area = rect1.width * rect1.height;
        var rect2Area = rect2.width * rect2.height;

        int totalArea = rect1Area + rect2Area;

        int imagesForRect1 = (int)Math.Round((double)rect1Area / totalArea * images.Count);
        int imagesForRect2 = images.Count - imagesForRect1;
        return (imagesForRect1, imagesForRect2);
    }

    public static int FindOverlappingAdArea(int _startx, int _starty, int _width, int _height, PageInfo pinfo)
    {
        int _area = 0;
        Rectangle r = new Rectangle() { x = _startx, y = _starty, width = _width, height = _height };
        foreach (var _ad in pinfo.ads)
        {
            //Treating all ads on single page as we have already adjusted the x in _allads list
            if (AreIntersected(r, _ad))
            {
                int _adstartx = _ad.newx;
                int _adstarty = _ad.newy;
                int _adendy = _ad.newy + _ad.newheight;
                int _adendx = _ad.newx + _ad.newwidth;

                if (_adendy > _starty + _height)
                    _adendy = _starty + _height;
                //Left page
                if (_ad.newx < ModelSettings.canvaswidth && _adstartx < _startx)
                {
                    _adstartx = _startx;
                }
                //Right Page
                if (_ad.newx >= ModelSettings.canvaswidth && _adendx > _startx + _width)
                {
                    _adendx = _startx + _width;
                }
                _area += (_adendx - _adstartx) * (_adendy - _adstarty);
            }
        }
        return _area;
    }

    public static double GetEffectiveWidth(Image img, bool isMugshot)
    {
        return (isMugshot && img?.imageMetadata?.doubleSize != null)
            ? img.imageMetadata.doubleSize.Width
            : img.width;
    }

    public static double GetEffectiveHeight(Image img, bool isMugshot)
    {
        return (isMugshot && img?.imageMetadata?.doubleSize != null)
            ? img.imageMetadata.doubleSize.Length
            : img.length;
    }
    public static Func<Image, bool> GetSubImageFilter(bool noFilter, Image _nimg)
    {
        if (noFilter)
        {
            return x => true;
        }

        return ModelSettings.samesizesubimageallowed
            ? (Func<Image, bool>)(x => x.width <= _nimg.width)
            : (x => x.width < _nimg.width);
    }

    public static int FindOverlappingAdAreaSingePage(int _startx, int _starty, int _width, int _height, PageInfo pinfo)
    {
        int _area = 0;
        Rectangle r = new Rectangle() { x = _startx, y = _starty, width = _width, height = _height };
        foreach (var _ad in pinfo.ads)
        {
            //Treating all ads on single page as we have already adjusted the x in _allads list
            if (AreIntersected(r, _ad))
            {
                int _adstartx = _ad.newx;
                int _adstarty = _ad.newy;
                int _adendy = _ad.newy + _ad.newheight;
                int _adendx = _ad.newx + _ad.newwidth;

                if (_adendy > _starty + _height)
                    _adendy = _starty + _height;
                //Left page
                if (_ad.newx < ModelSettings.canvaswidth && _adstartx < _startx)
                {
                    _adstartx = _startx;
                }
                if (_adendx > _startx + _width)
                {
                    _adendx = _startx + _width;
                }
                _area += (_adendx - _adstartx) * (_adendy - _adstarty);
            }
        }
        return _area;
    }

    public static void ConvertEditorialSpaceBelowAdToAd(ref PageInfo _info, int numlines)
    {
        for (int i=0; i<ModelSettings.canvaswidth; i++)
        {
            int _maxeditorialline = 0;
            if (_info.paintedCanvas.Count(x => x.Key.Key == i && x.Value == FlowElements.Editorial)>0)
                _maxeditorialline= _info.paintedCanvas.Where(x => x.Key.Key == i && x.Value == FlowElements.Editorial).Max(x => x.Key.Value);
            int _maxAdline = 0;
            if (_info.paintedCanvas.Count(x => x.Key.Key == i && x.Value != FlowElements.Editorial)>0)
                _maxAdline = _info.paintedCanvas.Where(x => x.Key.Key == i && x.Value != FlowElements.Editorial).Max(x => x.Key.Value);
            if (_maxeditorialline == ModelSettings.canvasheight-1 && _maxeditorialline-_maxAdline <=2)
            {
                for (int j= _maxAdline + 1; j<ModelSettings.canvasheight; j++)
                {
                    _info.paintedCanvas.Remove(new KeyValuePair<int, int>(i, j));
                    _info.paintedCanvas.Add(new KeyValuePair<int, int>(i, j), FlowElements.Ad);

                }

            }

        }
    }

    public static void GeneratePngFiles(string laydownPath, string outputFolderPath)
    {
        try
        {
            PngExtension.Generate(laydownPath, outputFolderPath);
        }
        catch (Exception ex)
        {
            Log.Warning("Exception occured while PNG files");
            Log.Warning(ex.Message);

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                Log.Warning(ex.StackTrace);
        }
    }

    public static void RepositionTheArticles(List<ScoreList> _scorelist)
    {
        try
        {
            foreach (ScoreList scorelist in _scorelist)
            {
                if (scorelist != null && scorelist.boxes != null && scorelist.boxes.Count(x => x.width == 1) > 1)
                {
                    if (scorelist.boxes.Count(x => x.width > 1 && x.position.pos_x + x.width > 4) > 0)
                        continue;
                    List<Box> _fifthcolumnboxes = scorelist.boxes.Where(x => x.position.pos_x == 4).ToList();
                    List<Box> _sixthcolumnboxes = scorelist.boxes.Where(x => x.position.pos_x == 5).ToList();

                    if (_sixthcolumnboxes == null || _sixthcolumnboxes.Count == 0)
                        continue;
                    if (_fifthcolumnboxes == null || _fifthcolumnboxes.Count == 0)
                        continue;

                    int _maxendy = _fifthcolumnboxes.Max(x => x.position.pos_z + (int)x.length);
                    List<Box> _firstcolumnboxes = scorelist.boxes.Where(x => x.position.pos_x == 0 && x.position.pos_z <= _maxendy).ToList();
                    if (_firstcolumnboxes == null || _firstcolumnboxes.Count == 0)
                        continue;


                    _fifthcolumnboxes.ForEach(x => x.position.pos_x = 0);
                    _fifthcolumnboxes.ForEach(x => x.pos_x = 0);
                    _firstcolumnboxes.ForEach(x => x.position.pos_x = 1);
                    _firstcolumnboxes.ForEach(x => x.pos_x = 1);

                }

            }
        }
        catch (Exception e)
        {
            Log.Error("Error while RepositionTheArticles: {msg}", e.StackTrace);
        }
    }

    public static void AdjustTheAdwrapStory(ScoreList sc)
    {
        try
        {
            if (sc == null || sc.boxes == null || sc.boxes.Count <= 1)
                return;

            Box adwrapstory = sc.boxes.FirstOrDefault(x => x.articletype == "adwrap");
            if (adwrapstory == null)
                return;

            if (adwrapstory.placementposition == FlowPacker.TopLeft || adwrapstory.placementposition == FlowPacker.TopRight)
                return;

            var boxes = sc.boxes.Where(x => x.Id != adwrapstory.Id && (adwrapstory.position.pos_x < x.position.pos_x + x.width)
                && (adwrapstory.position.pos_x + adwrapstory.width > x.position.pos_x)).ToList();
            if (boxes != null && boxes.Count>0)
            {

                int _endy = (int)boxes.Max(x => x.position.pos_z + x.length);

                if (adwrapstory.position.pos_z - _endy > ModelSettings.articleseparatorheight)
                {
                    int _delta = _endy + ModelSettings.articleseparatorheight - adwrapstory.position.pos_z;
                    AdjustTheBoxElementPosition(adwrapstory.position, _delta);
                    AdjustTheBoxElementPosition(adwrapstory.kickerPosition, _delta);
                    AdjustTheBoxElementPosition(adwrapstory.preamblePosition, _delta);
                    AdjustTheBoxElementPosition(adwrapstory.bylinePosition, _delta);
                    AdjustTheBoxElementPosition(adwrapstory.headlinePosition, _delta);
                    foreach (var _img in adwrapstory.usedImageList)
                        AdjustTheBoxElementPosition(_img.position, _delta);

                }
            }
        }
        catch(Exception e)
        {
            Log.Write(Serilog.Events.LogEventLevel.Error, "Error while Adjusting the Adwrap story: {ms}", e.StackTrace);
        }
        
    }

    private static void AdjustTheBoxElementPosition(Node elementposition, int _deltaz)
    {
        if (elementposition != null)
            elementposition.pos_z = elementposition.pos_z + _deltaz;
    }

}

public class BigIntegerConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return BigInteger.Parse(reader.GetString() ?? "0");
    }

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
