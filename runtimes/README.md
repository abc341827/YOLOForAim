# TensorRT runtime folders

`TensorRtNative.dll` is loaded dynamically from one of these folders before falling back to the application base directory.

Supported selector values:

- `cuda13`, `trt11-cuda13`, `tensorrt11-cuda13`
- `cuda12`, `trt11-cuda12`, `tensorrt11-cuda12`
- `trt10-cuda12`, `tensorrt10-cuda12`
- `base` or `root`
- `auto`

Configure the runtime with either:

1. Environment variable `YOLOFORAIM_TENSORRT_RUNTIME`, or
2. A `tensorrt-runtime.txt` file next to `YOLOForAim.exe` containing one selector value.

Default `auto` order:

1. application base directory
2. `runtimes\tensorrt11-cuda13`
3. `runtimes\tensorrt11-cuda12`
4. `runtimes\tensorrt10-cuda12`

## Folder layout

`runtimes\tensorrt11-cuda13` should contain the CUDA 13 build, for example:

- `TensorRtNative.dll`
- `nvinfer_11.dll`
- `nvinfer_plugin_11.dll`
- `cudart64_13.dll` if `TensorRtNative.dll` lists it as a dependency
- other CUDA/TensorRT dependencies reported by Dependencies.exe

`runtimes\tensorrt11-cuda12` should contain the CUDA 12 build, for example:

- `TensorRtNative.dll`
- `nvinfer_11.dll`
- `nvinfer_plugin_11.dll`
- `cudart64_12.dll`
- other CUDA/TensorRT dependencies reported by Dependencies.exe

`runtimes\tensorrt10-cuda12` can be used for the old TensorRT 10/CUDA 12 build if needed.

Runtime DLLs are intentionally ignored by git.
