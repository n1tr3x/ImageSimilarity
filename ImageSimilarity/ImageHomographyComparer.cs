using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Util;
using Emgu.CV;

namespace ImageSimilarity
{

    public class ImageHomographyComparer
    {
        /// <summary>
        /// Единая целевая длина по длинной стороне для обоих изображений.
        /// Масштабируем даже если изображение меньше — иначе ORB даёт мало совпадений.
        /// </summary>
        public int TargetLongSide { get; set; } = 600;
        public int NumberOfFeatures { get; set; } = 1500;
        public float ScaleFactor { get; set; } = 1.2f;
        public int NLevels { get; set; } = 8;
        public int EdgeThreshold { get; set; } = 31;
        public int FirstLevel { get; set; } = 0;

        public double LoweRatio { get; set; } = 0.75;
        public double RansacReprojThreshold { get; set; } = 3.0;

        public HomographyResult CompareFiles(string sourcePath, string searchPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("sourcePath is null or empty", nameof(sourcePath));

            if (string.IsNullOrWhiteSpace(searchPath))
                throw new ArgumentException("searchPath is null or empty", nameof(searchPath));

            using var srcColor = CvInvoke.Imread(sourcePath);
            using var dstColor = CvInvoke.Imread(searchPath);

            if (srcColor.IsEmpty)
                throw new InvalidOperationException($"Failed to load the image: {sourcePath}");

            if (dstColor.IsEmpty)
                throw new InvalidOperationException($"Failed to load the image: {searchPath}");

            return CompareMats(srcColor, dstColor);
        }

        public HomographyResult CompareMats(Mat srcColor, Mat dstColor)
        {
            if (srcColor == null || srcColor.IsEmpty)
                throw new ArgumentException("srcColor is null or empty", nameof(srcColor));

            if (dstColor == null || dstColor.IsEmpty)
                throw new ArgumentException("dstColor is null or empty", nameof(dstColor));

            // нормализуем к одному масштабу
            using var srcGray = PreprocessToUnifiedScale(srcColor, TargetLongSide);
            using var dstGray = PreprocessToUnifiedScale(dstColor, TargetLongSide);

            using var orb = new ORB(
                numberOfFeatures: NumberOfFeatures,
                scaleFactor: ScaleFactor,
                nLevels: NLevels,
                edgeThreshold: EdgeThreshold,
                firstLevel: FirstLevel
            );

            using var keypoints1 = new VectorOfKeyPoint();
            using var descriptors1 = new Mat();
            using var keypoints2 = new VectorOfKeyPoint();
            using var descriptors2 = new Mat();

            orb.DetectAndCompute(srcGray, null, keypoints1, descriptors1, false);
            orb.DetectAndCompute(dstGray, null, keypoints2, descriptors2, false);

            if (descriptors1.Rows == 0 || descriptors2.Rows == 0)
            {
                return new HomographyResult
                {
                    TotalMatches = 0,
                    InliersCount = 0,
                    InliersRatio = 0.0,
                    SimilarityScore = 0.0
                };
            }

            using var goodMatches = MatchDescriptors(descriptors1, descriptors2, LoweRatio);

            if (goodMatches.Size < 4)
            {
                return new HomographyResult
                {
                    TotalMatches = goodMatches.Size,
                    InliersCount = 0,
                    InliersRatio = 0.0,
                    SimilarityScore = 0.0
                };
            }

            var result = ComputeHomographyAndScore(
                keypoints1,
                keypoints2,
                goodMatches,
                RansacReprojThreshold
            );

            return result;
        }

        /// <summary>
        /// Приводим изображение к одинаковому масштабу (по длинной стороне).
        /// Увеличиваем и уменьшаем - обязательно для стабильной работы ORB.
        /// </summary>
        private static Mat PreprocessToUnifiedScale(Mat src, int targetLongSide)
        {
            var gray = new Mat();
            CvInvoke.CvtColor(src, gray, ColorConversion.Bgr2Gray);

            int width = gray.Width;
            int height = gray.Height;
            int maxDim = Math.Max(width, height);

            // вычисляем масштаб
            double scale = (double)targetLongSide / maxDim;
            int newW = (int)(width * scale);
            int newH = (int)(height * scale);

            var resized = new Mat();
            CvInvoke.Resize(gray, resized, new System.Drawing.Size(newW, newH), 0, 0, Inter.Linear);
            gray.Dispose();
            return resized;
        }

        private static VectorOfDMatch MatchDescriptors(Mat desc1, Mat desc2, double loweRatio)
        {
            var good = new VectorOfDMatch();

            using var matcher = new BFMatcher(DistanceType.Hamming, crossCheck: false);
            using var matchesKnn = new VectorOfVectorOfDMatch();

            matcher.KnnMatch(desc1, desc2, matchesKnn, k: 2, null);

            for (int i = 0; i < matchesKnn.Size; i++)
            {
                var match = matchesKnn[i];
                if (match.Size < 2) continue;

                var m1 = match[0];
                var m2 = match[1];

                if (m1.Distance < loweRatio * m2.Distance)
                {
                    good.Push(new[] { m1 });
                }
            }

            return good;
        }

        private static HomographyResult ComputeHomographyAndScore(
            VectorOfKeyPoint kpts1,
            VectorOfKeyPoint kpts2,
            VectorOfDMatch matches,
            double ransacReprojThreshold)
        {
            var pts1 = new VectorOfPointF();
            var pts2 = new VectorOfPointF();

            var kps1 = kpts1.ToArray();
            var kps2 = kpts2.ToArray();
            var matchesArray = matches.ToArray();

            foreach (var m in matchesArray)
            {
                pts1.Push(new[] { kps1[m.QueryIdx].Point });
                pts2.Push(new[] { kps2[m.TrainIdx].Point });
            }

            int total = matchesArray.Length;
            int inliers = 0;
            double inlierRatio = 0.0;

            if (pts1.Size >= 4 && pts2.Size >= 4)
            {
                using var mask = new Mat();

                var homography = CvInvoke.FindHomography(
                    pts1,
                    pts2,
                    RobustEstimationAlgorithm.Ransac,
                    ransacReprojThreshold,
                    mask
                );

                if (homography != null && !homography.IsEmpty &&
                    mask != null && !mask.IsEmpty &&
                    mask.Rows == total)
                {
                    var maskDataObj = mask.GetData();

                    if (maskDataObj is byte[,] maskData2d)
                    {
                        for (int i = 0; i < mask.Rows; i++)
                        {
                            if (maskData2d[i, 0] != 0)
                                inliers++;
                        }
                    }

                    if (total > 0)
                        inlierRatio = (double)inliers / total;
                }
            }

            double score = 0.0;

            if (total >= 4)
            {
                if (total < 30)
                {
                    // Мало матчей — считаем это слабым сигналом,
                    // дополнительно душим через квадрат.
                    double t = (double)total / 30.0;
                    score = inlierRatio * t * t;
                }
                else
                {
                    // Сила совпадения по количеству матчей: 30 -> 0, 200+ -> 1
                    double matchStrength = (Math.Min(total, 200) - 30) / 170.0;
                    if (matchStrength < 0) matchStrength = 0;

                    // Итог: в основном смотрим на качество (inlierRatio),
                    // немного учитываем количество (matchStrength).
                    score = inlierRatio * 0.7 + matchStrength * 0.3;
                }
            }

            // Гарантируем диапазон [0;1]
            if (score < 0) score = 0;
            if (score > 1) score = 1;

            return new HomographyResult
            {
                TotalMatches = total,
                InliersCount = inliers,
                InliersRatio = inlierRatio,
                SimilarityScore = score
            };
        }

        public class HomographyResult
        {
            public int TotalMatches { get; set; }
            public int InliersCount { get; set; }
            public double InliersRatio { get; set; }
            public double SimilarityScore { get; set; }

            public bool IsSameOrigin(double scoreThreshold = 0.5, int minInliers = 15)
            {
                return SimilarityScore >= scoreThreshold && InliersCount >= minInliers;
            }
        }
    }
}
