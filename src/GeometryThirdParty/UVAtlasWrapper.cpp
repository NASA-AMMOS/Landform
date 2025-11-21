#include "UVAtlasWrapper.h"

#include "UVAtlas.h"

#include "DirectXMesh.h"

#include <stdio.h>

#include <vector>

extern "C" UVAtlasResult* UVAtlas_Compute(UVAtlasMesh* mesh, int maxCharts, float maxStretch, float gutter,
                                          int width, int height, uint32_t options, float adjacencyEpsilon,
                                          int& returnCode)
{
	returnCode = 1;

    std::vector<DirectX::XMFLOAT3> positions(mesh->numVertices);
	for (uint32_t i = 0; i < mesh->numVertices; i++) {
		positions[i] = DirectX::XMFLOAT3(mesh->xs[i], mesh->ys[i], mesh->zs[i]);
	}

    std::vector<uint32_t> adjacency(3 * mesh->numFaces);
    HRESULT hr = DirectX::GenerateAdjacencyAndPointReps(mesh->indices, mesh->numFaces, &positions[0], mesh->numVertices,
                                                        adjacencyEpsilon,
                                                        nullptr /*pointRep will be allocated internally*/,
                                                        &adjacency[0]);
	if (FAILED(hr))
	{
		fprintf(stderr, "ERROR: UVAtlas failed to generate adjacency (%08X)\n", hr);
		returnCode = 2;
		return nullptr;
	}

    //UVAtlas API requires this outIndices to be uint8_t even though the indices are uint32_t
    //the reason appears to be that it supports either 16 or 32 bit indices
	std::vector<DirectX::UVAtlasVertex> outVertices;
	std::vector<uint8_t> outIndices;
	std::vector<uint32_t> outVertexRemap;
	float outMaxStretch = 0;
	size_t outNumCharts = 0;
	hr = UVAtlasCreate(&positions[0], mesh->numVertices, mesh->indices, DXGI_FORMAT_R32_UINT, mesh->numFaces,
                       maxCharts, maxStretch, width, height, gutter,
                       &adjacency[0],
                       nullptr, // falseEdgeAdjacency
                       nullptr, // pIMTArray
                       nullptr, //statusCallback
                       0, //callbackFrequency
                       static_cast<DirectX::UVATLAS>(options),
                       outVertices,
                       outIndices,
                       nullptr, // outFacePartitioning
                       &outVertexRemap,
                       &outMaxStretch,
                       &outNumCharts);
	if (FAILED(hr))
	{
		fprintf(stderr, "ERROR: UVAtlas failed to generate atlas (%08X)\n", hr);
		returnCode = 3;
		return nullptr;
	}

	UVAtlasResult* result = new UVAtlasResult();

    const uint32_t outVertexCount = static_cast<uint32_t>(outVertices.size());
	result->numVertices = outVertexCount;
	result->us = new float[outVertexCount];
	result->vs = new float[outVertexCount];
	for (uint32_t i = 0; i < outVertexCount; i++) {
		result->us[i] = outVertices[i].uv.x;
		result->vs[i] = outVertices[i].uv.y;
	}

	const uint32_t outIndexCount = static_cast<uint32_t>(outIndices.size()) / sizeof(uint32_t);
	result->numFaces = outIndexCount / 3;
	result->indices = new uint32_t[outIndexCount];
	for (uint32_t i = 0; i < outIndexCount; i++) {
		result->indices[i] = reinterpret_cast<uint32_t*>(&outIndices[0])[i];
	}

    const uint32_t outRemapCount = static_cast<uint32_t>(outVertexRemap.size());
	result->vertexRemap = new uint32_t[outRemapCount];
	for (size_t i = 0; i < outVertexRemap.size(); i++) {
		result->vertexRemap[i] = outVertexRemap[i];
	}

	returnCode = 0;

	return result;
}

extern "C" void UVAtlas_Delete(UVAtlasResult* result)
{
	delete result->us;
	delete result->vs;
	delete result->indices;
	delete result->vertexRemap;
    delete result;
}
