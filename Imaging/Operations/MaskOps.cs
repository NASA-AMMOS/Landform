using System;
using System.Collections.Generic;

namespace JPLOPS.Imaging
{
    public static class MaskOps
    {
        public delegate void InvalidBlock(int blockRow, int blockCol, double validRatio);

        /// <summary>
        /// Count the number of valid (i.e. un-masked) pixels in each blocksize x blocksize chunk of this image.
        /// For each chunk where the ratio of the number of valid pixels to total in block is less than minValidRatio,
        /// invalidate (i.e. mask) all pixels in the block.
        /// Operates on the image in-place.
        /// If callback is provided then it is called for each invalid block instead of actually invalidating the block.
        /// </summary>
        public static Image InvalidateSparseBlocks(this Image img, int blocksize, double minValidRatio,
                                                   InvalidBlock callback = null)
        {
            if (img.HasMask)
            {
                int hBlocks = (int)Math.Ceiling(((double)img.Width) / blocksize);
                int vBlocks = (int)Math.Ceiling(((double)img.Height) / blocksize);
                for (int vBlock = 0; vBlock < vBlocks; vBlock++)
                {
                    int maxR = Math.Min(img.Height, (vBlock + 1) * blocksize);
                    for (int hBlock = 0; hBlock < hBlocks; hBlock++)
                    {
                        int maxC = Math.Min(img.Width, (hBlock + 1) * blocksize);
                        int numValid = 0, numTotal = 0;
                        for (int r = vBlock * blocksize; r < maxR; r++)
                        {
                            for (int c = hBlock * blocksize; c < maxC; c++)
                            {
                                numTotal++;
                                if (img.IsValid(r, c))
                                {
                                    numValid++;
                                }
                            }
                        }

                        if (numTotal > 0)
                        {
                            double ratio = ((double)numValid) / numTotal;
                            if (ratio < minValidRatio)
                            {
                                if (callback != null)
                                {
                                    callback(vBlock, hBlock, ratio);
                                }
                                else
                                {
                                    for (int r = vBlock * blocksize; r < maxR; r++)
                                    {
                                        for (int c = hBlock * blocksize; c < maxC; c++)
                                        {
                                            img.SetMaskValue(r, c, true);
                                        }
                                    }
                                }
                            }
                        }
                    } //for each block in row
                } //for each row of blocks
            } //has mask

            return img;
        }

        /// <summary>
        /// invalidate sparse blocks that are not fully surrounded by valid blocks
        /// </summary>
        public static Image InvalidateSparseExternalBlocks(this Image img, int blocksize, double minValidRatio)
        {
            if (!img.HasMask)
            {
                return img;
            }

            blocksize = Math.Max(blocksize, 1);

            int hBlocks = (int)Math.Ceiling(((double)img.Width) / blocksize);
            int vBlocks = (int)Math.Ceiling(((double)img.Height) / blocksize);

            //marked[row, col] = false means block is not invalid or has already been invalidated
            var marked = new bool[vBlocks, hBlocks];
            var seeds = new Queue<Pixel>(); //invalid border blocks

            //mark all invalid blocks and collect seeds
            img.InvalidateSparseBlocks(blocksize, minValidRatio,
                                       (row, col, ratio) => {
                                           marked[row, col] = true;
                                           if (row == 0 || row == vBlocks - 1 || col == 0 || col == hBlocks - 1)
                                           {
                                               seeds.Enqueue(new Pixel(row, col));
                                           }
                                       });

            var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };

            //DFS from each seed to invalidate blocks reachable from an invalid block on the image border
            while (seeds.Count > 0)
            {
                var seed = seeds.Dequeue();
                if (marked[seed.Row, seed.Col])
                {
                    marked[seed.Row, seed.Col] = false;
                    int maxR = Math.Min(img.Height, (seed.Row + 1) * blocksize);
                    int maxC = Math.Min(img.Width, (seed.Col + 1) * blocksize);
                    for (int r = seed.Row * blocksize; r < maxR; r++)
                    {
                        for (int c = seed.Col * blocksize; c < maxC; c++)
                        {
                            img.SetMaskValue(r, c, true);
                        }
                    }
                    foreach (var offset in offsets)
                    {
                        var n = seed + offset;
                        if (n.Row >= 0 && n.Row < vBlocks && n.Col >= 0 && n.Col < hBlocks && marked[n.Row, n.Col])
                        {
                            seeds.Enqueue(n);
                        }
                    }
                }
            }

            return img;
        }

        /// <summary>
        /// invalidate all but the largest blobs of valid (i.e. un-masked) pixels
        /// keeps the largest blob and all other blobs within tolerance of size of largest
        /// operates on the image in-place
        /// </summary>
        public static Image InvalidateAllButLargestValidBlobs(this Image img, double tolerance = 0.2)
        {
            if (!img.HasMask)
            {
                return img;
            }

            var marked = img.InstantiateBinaryImage(img.Width, img.Height);

            var seeds = new Queue<Pixel>();
            var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };
            int markBlob(Pixel seed)
            {
                int size = 0;
                seeds.Enqueue(seed);
                while (seeds.Count > 0)
                {
                    var px = seeds.Dequeue();
                    if (!marked[px.Row, px.Col])
                    {
                        size++;
                        marked[px.Row, px.Col] = true;
                        foreach (var offset in offsets)
                        {
                            var n = px + offset;
                            if (n.Row >= 0 && n.Row < img.Height && n.Col >= 0 && n.Col < img.Width &&
                                !marked[n.Row, n.Col] &&
                                img.IsValid(n.Row, n.Col))
                            {
                                seeds.Enqueue(n);
                            }
                        }
                    }
                }
                return size;
            }

            int largestBlobSize = 0;
            var blobs = new Dictionary<Pixel, int>(); //seed -> size

            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsValid(row, col) && !marked[row, col])
                    {
                        var seed = new Pixel(row, col);
                        int size = markBlob(seed);
                        blobs[seed] = size;
                        if (size > largestBlobSize)
                        {
                            largestBlobSize = size;
                        }
                    }
                }
            }

            if (blobs.Count > 0)
            {
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        marked[row, col] = false;
                    }
                }
                foreach (var blob in blobs)
                {
                    if (blob.Value >= tolerance * largestBlobSize)
                    {
                        markBlob(blob.Key);
                    }
                }
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (!marked[row, col])
                        {
                            img.SetMaskValue(row, col, true);
                        }
                    }
                }
            }
            return img;
        }

        public static Image MaskToImage(this Image img, float valid = 0, float invalid = 1)
        {
            var ret = img.Instantiate(1, img.Width, img.Height);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    ret[0, row, col] = img.IsValid(row, col) ? valid : invalid;
                }
            }
            return ret;
        }

        /// <summary>
        /// count valid pixels
        /// if mask image is provided then any pixels which are 0 there are also considered invalid
        /// </summary>
        public static int CountValid(this Image img, Image mask = null)
        {
            int valid = 0;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsValid(row, col) && (mask == null || mask[0, row, col] != 0))
                    {
                        valid++;
                    }
                }
            }
            return valid;
        }

        /// <summary>
        /// flood fill mask from each invalid pixel on the border of this image
        /// </summary>
        public static Image AddOuterRegionsToMask(this Image img, Image mask, float invalid = 1)
        {
            if (!img.HasMask)
            {
                return mask;
            }
            void floodFill(int row, int col)
            {
                if (img.IsValid(row, col) || mask[0, row, col] == invalid) return;
                mask[0, row, col] = invalid;
                var queue = new Queue<Pixel>();
                queue.Enqueue(new Pixel(row, col));
                var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };
                while (queue.Count > 0)
                {
                    var px = queue.Dequeue();
                    foreach (var offset in offsets)
                    {
                        var tgt = px + offset;
                        if (tgt.Row >= 0 && tgt.Row < img.Height && tgt.Col >= 0 && tgt.Col < img.Width &&
                            !img.IsValid(tgt.Row, tgt.Col) && mask[0, tgt.Row, tgt.Col] != invalid)
                        {
                            mask[0, tgt.Row, tgt.Col] = invalid;
                            queue.Enqueue(tgt);
                        }
                    }
                }
            }
            for (int row = 0; row < img.Height; row++)
            {
                floodFill(row, 0);
                floodFill(row, img.Width - 1);
            }
            for (int col = 0; col < img.Width; col++)
            {
                floodFill(0, col);
                floodFill(img.Height - 1, col);
            }
            return mask;
        }
    }
}

