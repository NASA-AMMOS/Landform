#pragma once

#include <stdint.h>

#include <stdio.h>
#include <stdlib.h>
#include <assert.h>
#include <conio.h>

#pragma pack(push,1)
struct UVAtlasData {
	uint32_t numVertices = 0;
	float* us;
	float* vs;
	float* xs;
	float* ys;
	float* zs;

	uint32_t numFaces = 0;
	uint32_t* indices;
	
	uint32_t* vertexRemap;
};
#pragma pack(pop)

extern "C" __declspec(dllexport) UVAtlasData* __cdecl UVAtlas(UVAtlasData* data, int maxCharts, float maxStretch, float gutter, int width, int height, unsigned long uvOptions, float adjacencyEpsilon, int& returnCode);
extern "C" __declspec(dllexport) void __cdecl UVAtlasData_Destroy(UVAtlasData* data);
