#include "TensorRtNative.h"

#include <NvInfer.h>
#include <NvInferPlugin.h>
#include <cuda_fp16.h>
#include <cuda_runtime_api.h>

#include <algorithm>
#include <array>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace
{
    using namespace nvinfer1;

    constexpr int kMaxDims = 8;
    constexpr int kMaxNameLength = 128;

    class TrtLogger final : public ILogger
    {
    public:
        void log(Severity severity, AsciiChar const* msg) noexcept override
        {
            if (severity > Severity::kWARNING || msg == nullptr)
            {
                return;
            }
        }
    };

    TrtLogger gLogger;

    struct TensorBuffer
    {
        std::string name;
        bool isInput{};
        DataType dataType{DataType::kFLOAT};
        Dims dims{};
        int64_t elementCount{};
        size_t byteSize{};
        void* deviceBuffer{};
        std::vector<std::byte> hostRawBuffer;
        std::vector<float> hostFloatBuffer;
    };

    struct TensorRtDestroy
    {
        template <typename T>
        void operator()(T* value) const noexcept
        {
            if (value != nullptr)
            {
                delete value;
            }
        }
    };

    using RuntimePtr = std::unique_ptr<IRuntime, TensorRtDestroy>;
    using EnginePtr = std::unique_ptr<ICudaEngine, TensorRtDestroy>;
    using ContextPtr = std::unique_ptr<IExecutionContext, TensorRtDestroy>;

    struct EngineContext
    {
        RuntimePtr runtime;
        EnginePtr engine;
        ContextPtr context;
        cudaStream_t stream{};
        std::vector<TensorBuffer> tensors;
        std::vector<int> outputTensorIndices;

        ~EngineContext()
        {
            for (TensorBuffer& tensor : tensors)
            {
                if (tensor.deviceBuffer != nullptr)
                {
                    cudaFree(tensor.deviceBuffer);
                    tensor.deviceBuffer = nullptr;
                }
            }

            if (stream != nullptr)
            {
                cudaStreamDestroy(stream);
                stream = nullptr;
            }
        }
    };

    void SetError(char* errorBuffer, int errorBufferCapacity, const std::string& message) noexcept
    {
        if (errorBuffer == nullptr || errorBufferCapacity <= 0)
        {
            return;
        }

        std::size_t length = std::min<std::size_t>(message.size(), static_cast<std::size_t>(errorBufferCapacity - 1));
        std::memcpy(errorBuffer, message.data(), length);
        errorBuffer[length] = '\0';
    }

    std::string ToString(const std::exception& ex)
    {
        return ex.what() == nullptr ? std::string("unknown error") : std::string(ex.what());
    }

    int ElementTypeFromTensorRt(DataType dataType) noexcept
    {
        switch (dataType)
        {
        case DataType::kFLOAT:
            return 1;
        case DataType::kHALF:
            return 2;
        case DataType::kINT32:
            return 3;
        case DataType::kINT64:
            return 4;
        case DataType::kINT8:
            return 5;
        case DataType::kUINT8:
            return 6;
        case DataType::kBOOL:
            return 7;
        default:
            return 0;
        }
    }

    size_t GetElementSize(DataType dataType)
    {
        switch (dataType)
        {
        case DataType::kFLOAT:
            return sizeof(float);
        case DataType::kHALF:
            return sizeof(__half);
        case DataType::kINT32:
            return sizeof(int32_t);
        case DataType::kINT64:
            return sizeof(int64_t);
        case DataType::kINT8:
            return sizeof(int8_t);
        case DataType::kUINT8:
            return sizeof(uint8_t);
        case DataType::kBOOL:
            return sizeof(bool);
        default:
            throw std::runtime_error("Unsupported TensorRT data type.");
        }
    }

    int64_t GetElementCount(const Dims& dims)
    {
        int64_t count = 1;
        for (int index = 0; index < dims.nbDims; ++index)
        {
            if (dims.d[index] <= 0)
            {
                throw std::runtime_error("Dynamic tensor dimensions are not supported by this bridge yet.");
            }

            count *= static_cast<int64_t>(dims.d[index]);
        }

        return std::max<int64_t>(count, 1);
    }

    std::vector<std::byte> ReadAllBytes(const wchar_t* enginePath)
    {
        if (enginePath == nullptr || *enginePath == L'\0')
        {
            throw std::runtime_error("Engine path is empty.");
        }

        std::filesystem::path path(enginePath);
        std::ifstream stream(path, std::ios::binary | std::ios::ate);
        if (!stream)
        {
            throw std::runtime_error("Failed to open engine file.");
        }

        std::streamsize size = stream.tellg();
        if (size <= 0)
        {
            throw std::runtime_error("Engine file is empty.");
        }

        stream.seekg(0, std::ios::beg);
        std::vector<std::byte> bytes(static_cast<size_t>(size));
        if (!stream.read(reinterpret_cast<char*>(bytes.data()), size))
        {
            throw std::runtime_error("Failed to read engine file.");
        }

        return bytes;
    }

    void ThrowIfCudaFailed(cudaError_t error, const char* operation)
    {
        if (error == cudaSuccess)
        {
            return;
        }

        std::ostringstream stream;
        stream << operation << " failed: " << cudaGetErrorString(error);
        throw std::runtime_error(stream.str());
    }

    void ConvertInputToExpectedType(const float* inputData, int64_t inputElementCount, TensorBuffer& inputTensor, std::vector<std::byte>& convertedBuffer)
    {
        if (inputTensor.elementCount != inputElementCount)
        {
            std::ostringstream stream;
            stream << "Input element count mismatch. Expected " << inputTensor.elementCount << ", got " << inputElementCount;
            throw std::runtime_error(stream.str());
        }

        if (inputTensor.dataType == DataType::kFLOAT)
        {
            convertedBuffer.resize(static_cast<size_t>(inputElementCount) * sizeof(float));
            std::memcpy(convertedBuffer.data(), inputData, convertedBuffer.size());
            return;
        }

        if (inputTensor.dataType == DataType::kHALF)
        {
            convertedBuffer.resize(static_cast<size_t>(inputElementCount) * sizeof(__half));
            auto* destination = reinterpret_cast<__half*>(convertedBuffer.data());
            for (int64_t index = 0; index < inputElementCount; ++index)
            {
                destination[index] = __float2half_rn(inputData[index]);
            }
            return;
        }

        throw std::runtime_error("Only FLOAT/HALF TensorRT engine inputs are currently supported.");
    }

    void ConvertRawOutputToFloat(TensorBuffer& tensor)
    {
        tensor.hostFloatBuffer.resize(static_cast<size_t>(tensor.elementCount));

        switch (tensor.dataType)
        {
        case DataType::kFLOAT:
        {
            auto* source = reinterpret_cast<const float*>(tensor.hostRawBuffer.data());
            std::copy(source, source + tensor.elementCount, tensor.hostFloatBuffer.begin());
            break;
        }
        case DataType::kHALF:
        {
            auto* source = reinterpret_cast<const __half*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = __half2float(source[index]);
            }
            break;
        }
        case DataType::kINT32:
        {
            auto* source = reinterpret_cast<const int32_t*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = static_cast<float>(source[index]);
            }
            break;
        }
        case DataType::kINT64:
        {
            auto* source = reinterpret_cast<const int64_t*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = static_cast<float>(source[index]);
            }
            break;
        }
        case DataType::kINT8:
        {
            auto* source = reinterpret_cast<const int8_t*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = static_cast<float>(source[index]);
            }
            break;
        }
        case DataType::kUINT8:
        {
            auto* source = reinterpret_cast<const uint8_t*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = static_cast<float>(source[index]);
            }
            break;
        }
        case DataType::kBOOL:
        {
            auto* source = reinterpret_cast<const bool*>(tensor.hostRawBuffer.data());
            for (int64_t index = 0; index < tensor.elementCount; ++index)
            {
                tensor.hostFloatBuffer[static_cast<size_t>(index)] = source[index] ? 1.0f : 0.0f;
            }
            break;
        }
        default:
            throw std::runtime_error("Unsupported TensorRT output type.");
        }
    }

    std::unique_ptr<EngineContext> CreateEngineContext(const wchar_t* enginePath, TrtTensorInfo* tensorInfos, int tensorInfoCapacity, int* tensorCount)
    {
        std::vector<std::byte> engineBytes = ReadAllBytes(enginePath);

        initLibNvInferPlugins(&gLogger, "");

        RuntimePtr runtime(createInferRuntime(gLogger));
        if (!runtime)
        {
            throw std::runtime_error("Failed to create TensorRT runtime.");
        }

        EnginePtr engine(runtime->deserializeCudaEngine(engineBytes.data(), engineBytes.size()));
        if (!engine)
        {
            throw std::runtime_error("Failed to deserialize TensorRT engine.");
        }

        ContextPtr context(engine->createExecutionContext());
        if (!context)
        {
            throw std::runtime_error("Failed to create TensorRT execution context.");
        }

        int ioTensorCount = engine->getNbIOTensors();
        if (ioTensorCount <= 0)
        {
            throw std::runtime_error("TensorRT engine has no I/O tensors.");
        }

        if (ioTensorCount > tensorInfoCapacity)
        {
            throw std::runtime_error("TensorRT engine tensor count exceeds managed buffer capacity.");
        }

        auto engineContext = std::make_unique<EngineContext>();
        engineContext->runtime = std::move(runtime);
        engineContext->engine = std::move(engine);
        engineContext->context = std::move(context);
        engineContext->tensors.reserve(static_cast<size_t>(ioTensorCount));

        ThrowIfCudaFailed(cudaStreamCreate(&engineContext->stream), "cudaStreamCreate");

        for (int tensorIndex = 0; tensorIndex < ioTensorCount; ++tensorIndex)
        {
            const char* tensorName = engineContext->engine->getIOTensorName(tensorIndex);
            if (tensorName == nullptr)
            {
                throw std::runtime_error("TensorRT returned a null tensor name.");
            }

            TensorBuffer tensor;
            tensor.name = tensorName;
            tensor.isInput = engineContext->engine->getTensorIOMode(tensorName) == TensorIOMode::kINPUT;
            tensor.dataType = engineContext->engine->getTensorDataType(tensorName);
            tensor.dims = engineContext->engine->getTensorShape(tensorName);
            tensor.elementCount = GetElementCount(tensor.dims);
            tensor.byteSize = static_cast<size_t>(tensor.elementCount) * GetElementSize(tensor.dataType);
            tensor.hostRawBuffer.resize(tensor.isInput ? 0 : tensor.byteSize);
            tensor.hostFloatBuffer.resize(tensor.isInput ? 0 : static_cast<size_t>(tensor.elementCount));

            ThrowIfCudaFailed(cudaMalloc(&tensor.deviceBuffer, tensor.byteSize), "cudaMalloc");

            if (tensor.isInput)
            {
                if (!engineContext->context->setInputShape(tensorName, tensor.dims))
                {
                    throw std::runtime_error("Failed to set TensorRT input shape.");
                }
            }
            else
            {
                engineContext->outputTensorIndices.push_back(tensorIndex);
            }

            if (!engineContext->context->setTensorAddress(tensorName, tensor.deviceBuffer))
            {
                throw std::runtime_error("Failed to set TensorRT tensor address.");
            }

            TrtTensorInfo& tensorInfo = tensorInfos[tensorIndex];
            tensorInfo.isInput = tensor.isInput ? 1 : 0;
            tensorInfo.dataType = ElementTypeFromTensorRt(tensor.dataType);
            tensorInfo.nbDims = tensor.dims.nbDims;
            std::fill(std::begin(tensorInfo.dims), std::end(tensorInfo.dims), 0);
            for (int dimIndex = 0; dimIndex < tensor.dims.nbDims && dimIndex < kMaxDims; ++dimIndex)
            {
                tensorInfo.dims[dimIndex] = tensor.dims.d[dimIndex];
            }
            std::memset(tensorInfo.name, 0, kMaxNameLength);
            strncpy_s(tensorInfo.name, tensor.name.c_str(), _TRUNCATE);

            engineContext->tensors.push_back(std::move(tensor));
        }

        *tensorCount = ioTensorCount;
        return engineContext;
    }
}

extern "C"
{
    int trt_open_engine(
        const wchar_t* enginePath,
        TrtTensorInfo* tensorInfos,
        int tensorInfoCapacity,
        int* tensorCount,
        void** detectorHandle,
        char* errorBuffer,
        int errorBufferCapacity)
    {
        try
        {
            if (tensorInfos == nullptr || tensorCount == nullptr || detectorHandle == nullptr)
            {
                throw std::runtime_error("Managed buffers are null.");
            }

            auto engineContext = CreateEngineContext(enginePath, tensorInfos, tensorInfoCapacity, tensorCount);
            *detectorHandle = engineContext.release();
            SetError(errorBuffer, errorBufferCapacity, "");
            return 1;
        }
        catch (const std::exception& ex)
        {
            SetError(errorBuffer, errorBufferCapacity, ToString(ex));
            if (tensorCount != nullptr)
            {
                *tensorCount = 0;
            }
            if (detectorHandle != nullptr)
            {
                *detectorHandle = nullptr;
            }
            return 0;
        }
    }

    void trt_close_engine(void* detectorHandle)
    {
        auto* engineContext = reinterpret_cast<EngineContext*>(detectorHandle);
        delete engineContext;
    }

    int trt_run_inference(
        void* detectorHandle,
        const float* inputData,
        int64_t inputElementCount,
        char* errorBuffer,
        int errorBufferCapacity)
    {
        try
        {
            auto* engineContext = reinterpret_cast<EngineContext*>(detectorHandle);
            if (engineContext == nullptr || inputData == nullptr)
            {
                throw std::runtime_error("TensorRT detector handle or input buffer is null.");
            }

            TensorBuffer* inputTensor = nullptr;
            for (TensorBuffer& tensor : engineContext->tensors)
            {
                if (tensor.isInput)
                {
                    inputTensor = &tensor;
                    break;
                }
            }

            if (inputTensor == nullptr)
            {
                throw std::runtime_error("TensorRT detector has no input tensor.");
            }

            std::vector<std::byte> convertedInput;
            ConvertInputToExpectedType(inputData, inputElementCount, *inputTensor, convertedInput);
            ThrowIfCudaFailed(cudaMemcpyAsync(inputTensor->deviceBuffer, convertedInput.data(), convertedInput.size(), cudaMemcpyHostToDevice, engineContext->stream), "cudaMemcpyAsync(H2D)");

            if (!engineContext->context->enqueueV3(engineContext->stream))
            {
                throw std::runtime_error("TensorRT enqueueV3 failed.");
            }

            for (int outputTensorIndex : engineContext->outputTensorIndices)
            {
                TensorBuffer& outputTensor = engineContext->tensors[static_cast<size_t>(outputTensorIndex)];
                ThrowIfCudaFailed(cudaMemcpyAsync(outputTensor.hostRawBuffer.data(), outputTensor.deviceBuffer, outputTensor.byteSize, cudaMemcpyDeviceToHost, engineContext->stream), "cudaMemcpyAsync(D2H)");
            }

            ThrowIfCudaFailed(cudaStreamSynchronize(engineContext->stream), "cudaStreamSynchronize");

            for (int outputTensorIndex : engineContext->outputTensorIndices)
            {
                ConvertRawOutputToFloat(engineContext->tensors[static_cast<size_t>(outputTensorIndex)]);
            }

            SetError(errorBuffer, errorBufferCapacity, "");
            return 1;
        }
        catch (const std::exception& ex)
        {
            SetError(errorBuffer, errorBufferCapacity, ToString(ex));
            return 0;
        }
    }

    int trt_copy_output_to_float(
        void* detectorHandle,
        int outputIndex,
        float* destination,
        int64_t destinationElementCount,
        char* errorBuffer,
        int errorBufferCapacity)
    {
        try
        {
            auto* engineContext = reinterpret_cast<EngineContext*>(detectorHandle);
            if (engineContext == nullptr || destination == nullptr)
            {
                throw std::runtime_error("TensorRT detector handle or destination buffer is null.");
            }

            if (outputIndex < 0 || outputIndex >= static_cast<int>(engineContext->outputTensorIndices.size()))
            {
                throw std::runtime_error("TensorRT output index is out of range.");
            }

            TensorBuffer& outputTensor = engineContext->tensors[static_cast<size_t>(engineContext->outputTensorIndices[static_cast<size_t>(outputIndex)])];
            if (destinationElementCount < outputTensor.elementCount)
            {
                throw std::runtime_error("Managed output buffer is too small.");
            }

            std::memcpy(destination, outputTensor.hostFloatBuffer.data(), static_cast<size_t>(outputTensor.elementCount) * sizeof(float));
            SetError(errorBuffer, errorBufferCapacity, "");
            return 1;
        }
        catch (const std::exception& ex)
        {
            SetError(errorBuffer, errorBufferCapacity, ToString(ex));
            return 0;
        }
    }
}
