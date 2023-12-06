//--------------------------------------------------------------------------------------
// File: Mesh.cpp
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

#include "mesh.h"

#include <DirectXPackedVector.h>
#include <DirectXCollision.h>
#include "UVAtlas.h"

using namespace DirectX;

// Move constructor
Mesh::Mesh(Mesh&& moveFrom)
{
    *this = std::move(moveFrom);
}

// Move operator
Mesh& Mesh::operator= (Mesh&& moveFrom)
{
    if (this != &moveFrom)
    {
        mnFaces = moveFrom.mnFaces;
        mnVerts = moveFrom.mnVerts;
        mIndices.swap( moveFrom.mIndices );
        mAdjacency.swap( moveFrom.mAdjacency );
        mPositions.swap( moveFrom.mPositions );
        mNormals.swap( moveFrom.mNormals );
    }
    return *this;
}


//--------------------------------------------------------------------------------------
void Mesh::Clear()
{
    mnFaces = mnVerts = 0;

    // Release face data
    mIndices.reset();
    mAttributes.reset();
    mAdjacency.reset();

    // Release vertex data
    mPositions.reset();
    mNormals.reset();
}


//--------------------------------------------------------------------------------------
_Use_decl_annotations_
HRESULT Mesh::SetIndexData( size_t nFaces, const uint32_t* indices)
{
    if (!nFaces || !indices)
        return E_INVALIDARG;

    if ((uint64_t(nFaces) * 3) >= UINT32_MAX)
        return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);

    mnFaces = 0;
    mIndices.reset();
    mAttributes.reset();

    std::unique_ptr<uint32_t[]> ib(new (std::nothrow) uint32_t[nFaces * 3]);
    if (!ib)
        return E_OUTOFMEMORY;

    memcpy(ib.get(), indices, sizeof(uint32_t) * nFaces * 3);

    mIndices.swap(ib);
    mnFaces = nFaces;

    return S_OK;
}

//--------------------------------------------------------------------------------------
HRESULT Mesh::SetVertexData( float* xs, float* ys, float *zs, _In_ size_t nVerts )
{
    if ( !nVerts )
        return E_INVALIDARG;

    // Release vertex data
    mnVerts = 0;
    mPositions.reset();
    mNormals.reset();

    // Load positions (required)
    std::unique_ptr<XMFLOAT3[]> pos( new (std::nothrow) XMFLOAT3[ nVerts ] );
    if (!pos)
        return E_OUTOFMEMORY;
    
	for (size_t i = 0; i < nVerts; i++) {
		pos.get()[i] = XMFLOAT3(xs[i], ys[i], zs[i]);
	}
    
    // Load normals
    std::unique_ptr<XMFLOAT3[]> norms;

    // Return values
    mPositions.swap( pos );
    mNormals.swap( norms );
    mnVerts = nVerts;

    return S_OK;
}


//--------------------------------------------------------------------------------------
_Use_decl_annotations_
HRESULT Mesh::Validate(DWORD flags, std::wstring* msgs) const
{
    if (!mnFaces || !mIndices || !mnVerts)
        return E_UNEXPECTED;

    return DirectX::Validate(mIndices.get(), mnFaces, mnVerts, mAdjacency.get(), flags, msgs);
}


//--------------------------------------------------------------------------------------
HRESULT Mesh::Clean( _In_ bool breakBowties )
{
    if (!mnFaces || !mIndices || !mnVerts || !mPositions)
        return E_UNEXPECTED;

    std::vector<uint32_t> dups;
    HRESULT hr = DirectX::Clean(mIndices.get(), mnFaces, mnVerts, mAdjacency.get(), mAttributes.get(), dups, breakBowties);
    if (FAILED(hr))
        return hr;

    if (dups.empty())
    {
        // No vertex duplication is needed for mesh clean
        return S_OK;
    }

    size_t nNewVerts = mnVerts + dups.size();

    std::unique_ptr<XMFLOAT3[]> pos(new (std::nothrow) XMFLOAT3[nNewVerts]);
    if (!pos)
        return E_OUTOFMEMORY;

    memcpy(pos.get(), mPositions.get(), sizeof(XMFLOAT3) * mnVerts);

    std::unique_ptr<XMFLOAT3[]> norms;
    if (mNormals)
    {
        norms.reset(new (std::nothrow) XMFLOAT3[nNewVerts]);
        if (!norms)
            return E_OUTOFMEMORY;

        memcpy(norms.get(), mNormals.get(), sizeof(XMFLOAT3) * mnVerts);
    }

    size_t j = mnVerts;
    for (auto it = dups.begin(); it != dups.end() && (j < nNewVerts); ++it, ++j)
    {
        assert(*it < mnVerts);

        pos[ j ] = mPositions[ *it ];

        if (norms)
        {
            norms[ j ] = mNormals[ *it ];
        }
    }

    mPositions.swap(pos);
    mNormals.swap(norms);
    mnVerts = nNewVerts;

    return S_OK;
}


//--------------------------------------------------------------------------------------
HRESULT Mesh::GenerateAdjacency( _In_ float epsilon )
{
	if (!mnFaces || !mIndices || !mnVerts || !mPositions) {
		wprintf(L"!mnFaces || !mIndices || !mnVerts || !mPositions\n");
		return E_UNEXPECTED;
	}

	if ((uint64_t(mnFaces) * 3) >= UINT32_MAX) {
		wprintf(L"too many faces\n");
		return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
	}

    mAdjacency.reset( new (std::nothrow) uint32_t[ mnFaces * 3 ] );
	if (!mAdjacency) {
		wprintf(L"out of memory\n");
		return E_OUTOFMEMORY;		
	}
    return DirectX::GenerateAdjacencyAndPointReps(mIndices.get(), mnFaces, mPositions.get(), mnVerts, epsilon, nullptr, mAdjacency.get());
}


//--------------------------------------------------------------------------------------
HRESULT Mesh::ComputeNormals( _In_ DWORD flags )
{
    if (!mnFaces || !mIndices || !mnVerts || !mPositions)
        return E_UNEXPECTED;

    mNormals.reset( new (std::nothrow) XMFLOAT3[ mnVerts ] );
    if (!mNormals)
        return E_OUTOFMEMORY;

    return DirectX::ComputeNormals(mIndices.get(), mnFaces, mPositions.get(), mnVerts, flags, mNormals.get());
}
