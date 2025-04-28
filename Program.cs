using SixLabors.ImageSharp;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LibraryCacheReader
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Attempting to find Library file...");

            Guid localLowId = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
            string path = GetKnownFolderPath(localLowId) + "\\Against Gravity\\Rec Room\\Library";

            while(!File.Exists(path))
            {
                Console.WriteLine("Unable to find your Library file! Please drag the file onto this window and press Enter:");
                string? newPath = Console.ReadLine();

                if (string.IsNullOrEmpty(newPath))
                {
                    Console.WriteLine("No path provided!");
                    continue;
                }

                if(newPath.StartsWith("\"") && newPath.EndsWith("\""))
                {
                    newPath = newPath.Substring(1, newPath.Length - 2);
                }

                path = newPath;
            }

            Console.WriteLine($"Found Library file at: {path}\n\n");

            string text = Encoding.ASCII.GetString(File.ReadAllBytes(path));

            Regex urlRegex = new Regex(@"https?://[^\s""'?#]+\.(?:png|jpe?g)\b", RegexOptions.IgnoreCase);
            MatchCollection urlMatches = urlRegex.Matches(text);

            HashSet<string> seenData = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> links = new List<string>();

            SemaphoreSlim downloadSlim = new SemaphoreSlim(2);
            Random random = new Random();
            List<Task> downloadTasks = new List<Task>();

            string downloadDirectory = "DownloadedImages";
            Directory.CreateDirectory(downloadDirectory);

            string renderDirectory = Path.Combine(downloadDirectory, "Renders");
            Directory.CreateDirectory(renderDirectory);

            HttpClient client = new HttpClient();

            for (int i = 0; i < urlMatches.Count; i++)
            {
                Match match = urlMatches[i];
                string url = match.Value;

                if (!seenData.Add(url))
                    continue;

                links.Add(url);
            }

            // Save all links to a txt file 
            using(StreamWriter writer = new StreamWriter("output.txt"))
            {
                var groups = links
                    .GroupBy(x => Path.GetExtension(new Uri(x).AbsolutePath).ToLowerInvariant())
                    .OrderBy(y => y.Key);

                foreach (var extension in groups)
                {
                    writer.WriteLine($"Extension: {extension.Key}");
                    foreach(var url in extension)
                    {
                        writer.WriteLine($"{url}");
                    }

                    writer.WriteLine();
                }
            }

            // Download all images
            foreach(string url in links)
            {
                downloadTasks.Add(Task.Run(async () =>
                {
                    await downloadSlim.WaitAsync();
                    try
                    {
                        string filename = Path.GetFileName(new Uri(url).AbsolutePath);
                        string downloadPath = Path.Combine(downloadDirectory, filename);

                        string? file = Directory.EnumerateFiles(downloadDirectory, filename, SearchOption.AllDirectories).FirstOrDefault();

                        if (file != null)
                        {
                            Console.WriteLine($"Image already downloaded! Skipping: {filename}");
                            return;
                        }

                        int attempts = 0;
                        bool success = false;
                        while(attempts < 3 && !success)
                        {
                            try
                            {
                                using HttpResponseMessage response = await client.GetAsync(url);
                                response.EnsureSuccessStatusCode();
                                byte[] data = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(downloadPath, data);

                                Console.WriteLine($"Downloaded: {url}");
                                success = true;
                            }
                            catch (Exception ex) 
                            {
                                attempts++;
                                if (attempts < 3)
                                {
                                    Console.WriteLine($"Retry {attempts} for: {url}");
                                    await Task.Delay(1000);
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to download: {url}");
                                }
                            }
                        }

                        await Task.Delay(random.Next(500, 1000));

                    }
                    finally
                    {
                        downloadSlim.Release();
                    }
                }));
            }

            await Task.WhenAll(downloadTasks);
            Console.WriteLine("Downloaded all images! Moving...\n");

            // Moves all 2k images to a separate folder as these are usually renders.
            foreach (string filePath in Directory.GetFiles(downloadDirectory, "*.png"))
            {
                try
                {
                    ImageInfo image = Image.Identify(filePath);
                    if (image == null)
                        continue;

                    if(image.Width == 2048 && image.Height == 2048)
                    {
                        string destinationPath = Path.Combine(renderDirectory, Path.GetFileName(filePath));

                        if (!File.Exists(destinationPath))
                        {
                            File.Move(filePath, destinationPath);
                            Console.WriteLine($"Moved {Path.GetFileName(filePath)} to Renders folder.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                }
            }

            Console.WriteLine("\nFinished downloading/moving everything! You can close this now.");
            Console.ReadLine();
        }

        static string GetKnownFolderPath(Guid knownFolderId)
        {
            IntPtr pszPath = IntPtr.Zero;
            try
            {
                int hr = SHGetKnownFolderPath(knownFolderId, 0, IntPtr.Zero, out pszPath);
                if (hr >= 0)
                    return Marshal.PtrToStringAuto(pszPath);
                throw Marshal.GetExceptionForHR(hr);
            }
            finally
            {
                if (pszPath != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pszPath);
            }
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);
    }
}
