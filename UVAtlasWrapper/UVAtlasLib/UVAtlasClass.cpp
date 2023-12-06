#pragma once

#include "UVAtlasClass.h"

#include <stdio.h>
#include <stdlib.h>
#include <assert.h>
#include <conio.h>

#include <memory>
#include <list>

#include <dxgiformat.h>

#include "UVAtlas.h"
#include "directxtex.h"

#include "Mesh.h"
#include "UVAtlasClass.h"

#pragma warning(push)
#pragma warning(disable : 4005)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define NODRAWTEXT
#define NOGDI
#define NOBITMAP
#define NOMCX
#define NOSERVICE
#define NOHELP
#pragma warning(pop)

using namespace DirectX;

extern "C" __declspec(dllexport) UVAtlasData* __cdecl UVAtlas(UVAtlasData* data, int maxCharts, float maxStretch, float gutter, int width, int height, unsigned long uvOptions, float adjacencyEpsilon, int& returnCode)
{
	returnCode = 1;

	std::unique_ptr<Mesh> inMesh;

	inMesh.reset(new (std::nothrow) Mesh);
	 
	HRESULT hr = inMesh->SetIndexData(data->numFaces, data->indices);
	if (FAILED(hr)) {
		wprintf(L"\nERROR: Failed setting index data (%08X)\n", hr);
		returnCode = 2;
		return nullptr;
	}

	hr = inMesh->SetVertexData(data->xs, data->ys, data->zs, data->numVertices);
	if (FAILED(hr)) {
		wprintf(L"\nERROR: Failed setting vertex data (%08X)\n", hr);
		returnCode = 3;
		return nullptr;
	}

	// Prepare mesh for processing
	hr = inMesh->GenerateAdjacency(adjacencyEpsilon);
	if (FAILED(hr))
	{
		wprintf(L"\nERROR: Failed generating adjacency (%08X)\n", hr);
		returnCode = 4;
		return nullptr;
	}

	std::vector<UVAtlasVertex> vb;
	std::vector<uint8_t> ib;
	float outStretch = 0.f;
	size_t outCharts = 0;
	std::vector<uint32_t> facePartitioning;
	std::vector<uint32_t> vertexRemapArray;

	hr = UVAtlasCreate(inMesh->GetPositionBuffer(), inMesh->GetVertexCount(),
		inMesh->GetIndexBuffer(), DXGI_FORMAT_R32_UINT, data->numFaces,
		maxCharts, maxStretch, width, height, gutter,
		inMesh->GetAdjacencyBuffer(), nullptr,
		nullptr,
		0, 0.1f,
		uvOptions, vb, ib,
		nullptr,
		&vertexRemapArray,
		&outStretch, &outCharts);

	if (FAILED(hr))
	{
		wprintf(L"\nERROR: Failed generating Atlas (%08X)\n", hr);
		returnCode = 5;
		return nullptr;
	}
	UVAtlasData* result = new UVAtlasData;
	result->numVertices = (uint32_t)vb.size();
	result->us = new float[vb.size()];
	result->vs = new float[vb.size()];

	uint32_t* realIndices = reinterpret_cast<uint32_t*>(&ib[0]);
	size_t indexCount = ib.size() / sizeof(uint32_t);
	result->numFaces = (uint32_t)(indexCount / 3);
	result->indices = new uint32_t[indexCount];

	for (size_t i = 0; i < vb.size(); i++) {
		result->us[i] = vb[i].uv.x;
		result->vs[i] = vb[i].uv.y;
	}
	for (size_t i = 0; i < indexCount; i++) {
		result->indices[i] = realIndices[i];
	}
	result->vertexRemap = new uint32_t[vertexRemapArray.size()];
	for (size_t i = 0; i < vertexRemapArray.size(); i++) {
		result->vertexRemap[i] = vertexRemapArray[i];
	}
	returnCode = 0;
	return result;
}

extern "C" __declspec(dllexport) void __cdecl UVAtlasData_Destroy(UVAtlasData* data)
{
	delete data->indices;
	delete data->us;
	delete data->vs;
	delete data->vertexRemap;
	delete data;
}
