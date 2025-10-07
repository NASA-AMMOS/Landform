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

//_WIN32 is defined even on 64 bit Windows
#ifdef _WIN32
#define DLLEXPORT __declspec(dllexport) 
#else
#define DLLEXPORT
#endif

extern "C" DLLEXPORT UVAtlasResult* UVAtlas_Compute(UVAtlasMesh* mesh, int maxCharts, float maxStretch, float gutter,
                                                  int width, int height, uint32_t options, float adjacencyEpsilon,
                                                  int& returnCode);

extern "C" DLLEXPORT void UVAtlas_Delete(UVAtlasResult* data);

#undef DLLEXPORT
