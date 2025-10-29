#pragma once

#include <stdint.h>

#pragma pack(1)

struct UVAtlasMesh {
	uint32_t numVertices;
	uint32_t numFaces;
	float* xs;
	float* ys;
	float* zs;
	uint32_t* indices;
};

struct UVAtlasResult {
	uint32_t numVertices;
	uint32_t numFaces;
	float* us;
	float* vs;
	uint32_t* indices;
	uint32_t* vertexRemap;
};

#pragma pack()

#define UVATLAS_WRAPPER_API

//_WIN32 is defined even on 64 bit Windows
#ifdef _WIN32
#ifdef UVATLAS_WRAPPER_EXPORTS
#define UVATLAS_WRAPPER_API __declspec(dllexport) 
#else
#define UVATLAS_WRAPPER_API __declspec(dllimport) 
#endif
#endif

extern "C" UVATLAS_WRAPPER_API UVAtlasResult* UVAtlas_Compute(UVAtlasMesh* mesh, int maxCharts, float maxStretch,
                                                              float gutter, int width, int height, uint32_t options,
                                                              float adjacencyEpsilon, int& returnCode);

extern "C" UVATLAS_WRAPPER_API void UVAtlas_Delete(UVAtlasResult* data);

#undef UVATLAS_WRAPPER_API
