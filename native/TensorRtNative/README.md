# TensorRtNative

这个子项目用于在目标机上编译 `TensorRtNative.dll`，供 `YOLOForAim` 直接加载 `.engine`。

## 依赖

- TensorRT 10.x
- CUDA Toolkit
- Visual Studio C++ 工作负载

## 编译前设置

默认 `vcxproj` 使用：

- `$(TensorRtRoot)\include`
- `$(TensorRtRoot)\lib`
- `$(CudaToolkitDir)\include`
- `$(CudaToolkitDir)\lib\x64`

如果 TensorRT 不在 `C:\TensorRT`，请在目标机上修改：

- `native\TensorRtNative\TensorRtNative.vcxproj`
- 或给 `TensorRtRoot` 传入自定义 MSBuild 属性

## 输出

请将生成的 `TensorRtNative.dll` 复制到主程序输出目录，与 `YOLOForAim.exe`、`.engine` 同目录。

## 当前限制

- 当前桥接层只支持 **单输入** engine
- 当前桥接层要求输入/输出维度是 **静态维度**
- 当前桥接层会把输出统一转换成 `float[]` 返回给 C# 后处理
