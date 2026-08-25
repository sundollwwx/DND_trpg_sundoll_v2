using System;
using System.IO;
using UnityEngine;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Small platform seam for choosing an image without adding a native-file-picker package.
    /// The macOS path uses the system Open panel through osascript; other platforms keep the
    /// existing explicit-path input as the safe fallback until their native adapter is verified.
    /// </summary>
    public static class M4NativeFilePicker
    {
        public static bool TryPickImageFile(out string path, out string diagnostic)
        {
            path = string.Empty;
            diagnostic = string.Empty;

            if (UnityEngine.Application.platform != RuntimePlatform.OSXEditor &&
                UnityEngine.Application.platform != RuntimePlatform.OSXPlayer)
            {
                diagnostic = "当前平台没有已验证的原生文件选择器，请继续输入图片路径。";
                return false;
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = "-e \"POSIX path of (choose file with prompt \\\"选择棋子图片\\\" of type {\\\"public.image\\\"})\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                {
                    process.Start();
                    if (!process.WaitForExit(30000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // The timeout diagnostic remains the useful result for the caller.
                        }

                        diagnostic = "原生文件选择器超时。";
                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    var error = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    {
                        diagnostic = string.IsNullOrWhiteSpace(error) ? "已取消选择图片。" : "原生文件选择器失败：" + error;
                        return false;
                    }

                    path = output.TrimEnd('\r', '\n');
                    if (!File.Exists(path))
                    {
                        path = string.Empty;
                        diagnostic = "选择的图片路径不存在。";
                        return false;
                    }

                    diagnostic = "已选择图片文件。";
                    return true;
                }
            }
            catch (Exception exception)
            {
                diagnostic = "无法打开原生文件选择器：" + exception.Message;
                return false;
            }
        }
    }
}
