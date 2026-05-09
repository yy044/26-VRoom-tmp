// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.FaceDetector;
using UnityEngine;
using UnityEngine.Rendering;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

namespace Mediapipe.Unity.Sample.FaceDetection
{
  public class FaceTrackingRunner : VisionTaskApiRunner<FaceDetector>
  {
    [SerializeField] private DetectionResultAnnotationController _detectionResultAnnotationController;

    private Experimental.TextureFramePool _textureFramePool;

    private readonly object _latestResultLock = new object();
    private FaceDetectionResult _latestResult;
    private bool _hasLatestResult;

    public readonly FaceDetectionConfig config = new FaceDetectionConfig();

    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    public bool TryGetLatestResult(ref FaceDetectionResult result)
    {
      lock (_latestResultLock)
      {
        if (!_hasLatestResult || _latestResult.detections == null || _latestResult.detections.Count == 0)
        {
          return false;
        }

        _latestResult.CloneTo(ref result);
        return true;
      }
    }

    private void SaveLatestResult(FaceDetectionResult result)
    {
      lock (_latestResultLock)
      {
        _hasLatestResult = result.detections != null && result.detections.Count > 0;

        if (_hasLatestResult)
        {
          result.CloneTo(ref _latestResult);
        }
        else
        {
          _latestResult = default;
        }
      }
    }

    private void ClearLatestResult()
    {
      lock (_latestResultLock)
      {
        _hasLatestResult = false;
        _latestResult = default;
      }
    }

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
      ImageWidth = 0;
      ImageHeight = 0;
      ClearLatestResult();
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Model = {config.ModelName}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"MinDetectionConfidence = {config.MinDetectionConfidence}");
      Debug.Log($"MinSuppressionThreshold = {config.MinSuppressionThreshold}");
      Debug.Log($"NumFaces = {config.NumFaces}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetFaceDetectorOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnFaceDetectionsOutput : null);
      taskApi = FaceDetector.CreateFromOptions(options, GpuManager.GpuResources);
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      ImageWidth = imageSource.textureWidth;
      ImageHeight = imageSource.textureHeight;
      ClearLatestResult();

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      if (_detectionResultAnnotationController != null)
      {
          SetupAnnotationController(_detectionResultAnnotationController, imageSource);
      }

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = FaceDetectionResult.Alloc(options.numFaces);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return null;
          continue;
        }

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              textureFrame.Release();
              continue;
            }
            image = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              _detectionResultAnnotationController?.DrawNow(result);
              SaveLatestResult(result);
            }
            else
            {
              // clear the annotation
              _detectionResultAnnotationController?.DrawNow(default);
              ClearLatestResult();
            }
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              _detectionResultAnnotationController?.DrawNow(result);
              SaveLatestResult(result);
            }
            else
            {
              // clear the annotation
              _detectionResultAnnotationController?.DrawNow(default);
              ClearLatestResult();
            }
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }
      }
    }

    private void OnFaceDetectionsOutput(FaceDetectionResult result, Image image, long timestamp)
    {
      _detectionResultAnnotationController?.DrawLater(result);
      SaveLatestResult(result);
    }
  }
}
