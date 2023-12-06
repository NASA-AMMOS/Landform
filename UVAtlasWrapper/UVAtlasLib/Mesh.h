//--------------------------------------------------------------------------------------
// File: Mesh.h
//
// Mesh processing helper class
//
// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//
// http://go.microsoft.com/fwlink/?LinkID=324981
//--------------------------------------------------------------------------------------
#pragma once
#include <windows.h>

#include <memory>
#include <string>
#include <vector>

#include <stdint.h>

#if defined(_XBOX_ONE) && defined(_TITLE)
#include <d3d11_x.h>
#define DCOMMON_H_INCLUDED
#else
#include <d3d11_1.h>
#endif

#include <directxmath.h>

#include "directxmesh.h"

class Mesh
{
public:
    Mesh() : mnFaces(0), mnVerts(0) {}
    Mesh(Mesh&& moveFrom);
    Mesh& operator= (Mesh&& moveFrom);

    Mesh(Mesh const&) = delete;
    Mesh& operator= (Mesh const&) = delete;

    // Methods
    void Clear();

    HRESULT SetIndexData( _In_ size_t nFaces, _In_reads_(nFaces*3) const uint32_t* indices );

    HRESULT SetVertexData(float* xs, float* ys, float *zs, _In_ size_t nVerts );

    HRESULT Validate( _In_ DWORD flags, _In_opt_ std::wstring* msgs ) const;

    HRESULT Clean( _In_ bool breakBowties=false );

    HRESULT GenerateAdjacency( _In_ float epsilon );

    HRESULT ComputeNormals( _In_ DWORD flags );
    // Accessors
    const uint32_t* GetAttributeBuffer() const { return mAttributes.get(); }
    const uint32_t* GetAdjacencyBuffer() const { return mAdjacency.get(); }
    const DirectX::XMFLOAT3* GetPositionBuffer() const { return mPositions.get(); }

    size_t GetFaceCount() const { return mnFaces; }
    size_t GetVertexCount() const { return mnVerts; }

    const uint32_t* GetIndexBuffer() const { return mIndices.get(); }

private:
    size_t                                      mnFaces;
    size_t                                      mnVerts;
    std::unique_ptr<uint32_t[]>                 mIndices;
    std::unique_ptr<uint32_t[]>                 mAttributes;
    std::unique_ptr<uint32_t[]>                 mAdjacency;
    std::unique_ptr<DirectX::XMFLOAT3[]>        mPositions;
    std::unique_ptr<DirectX::XMFLOAT3[]>        mNormals;
};