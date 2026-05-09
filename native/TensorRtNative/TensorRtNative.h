#pragma once

#include <cstdint>

#ifdef TENSORRTNATIVE_EXPORTS
#define TENSORRTNATIVE_API __declspec(dllexport)
#else
#define TENSORRTNATIVE_API __declspec(dllimport)
#endif

extern "C"
{
    struct TrtTensorInfo
    {
        int32_t isInput;
        int32_t dataType;
        int32_t nbDims;
        int64_t dims[8];
        char name[128];
    };

    TENSORRTNATIVE_API int trt_open_engine(
        const wchar_t* enginePath,
        TrtTensorInfo* tensorInfos,
        int tensorInfoCapacity,
        int* tensorCount,
        void** detectorHandle,
        char* errorBuffer,
        int errorBufferCapacity);

    TENSORRTNATIVE_API void trt_close_engine(void* detectorHandle);

    TENSORRTNATIVE_API int trt_run_inference(
        void* detectorHandle,
        const float* inputData,
        int64_t inputElementCount,
        char* errorBuffer,
        int errorBufferCapacity);

    TENSORRTNATIVE_API int trt_copy_output_to_float(
        void* detectorHandle,
        int outputIndex,
        float* destination,
        int64_t destinationElementCount,
        char* errorBuffer,
        int errorBufferCapacity);
}
