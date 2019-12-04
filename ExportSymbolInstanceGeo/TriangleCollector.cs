﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;

namespace ExportSymbolInstanceGeo
{
  class TriangleCollector
  {
    #region Line and triangle indices
    class LineSegmentIndices
    {
      public int A { get; set; }
      public int B { get; set; }
      public LineSegmentIndices( int a, int b )
      {
        A = a;
        B = b;
      }
    }

    class TriangleIndices
    {
      public int A { get; set; }
      public int B { get; set; }
      public int C { get; set; }
      public TriangleIndices( int a, int b, int c )
      {
        A = a;
        B = b;
        C = c;
      }
      public override string ToString()
      {
        return string.Format( "{0} {1} {2}", A, B, C );
      }
    }
    #endregion // Line and triangle indices

    IntVertexLookup _vertices;
    List<LineSegmentIndices> _lines;
    List<TriangleIndices> _instance_triangles;
    Transform _symbol_transform;
    List<TriangleIndices> _symbol_triangles;
    List<Transform> _transformations;

    #region Transform stack
    void PushTransformation( Transform t )
    {
      Debug.Assert( null == _transformations,
        "currently only one level deep supported" );

      if( null == _transformations )
      {
        _transformations = new List<Transform>( 1 );
      }
      _transformations.Add( t );
    }

    void PopTransformation()
    {
      Debug.Assert( null != _transformations,
        "cannot pop transform from empty stack" );

      int n = _transformations.Count;

      if( 1 == n )
      {
        _transformations = null;
      }
      else
      {
        _transformations.RemoveAt( n - 1 );
      }
    }

    XYZ TransformPoint( XYZ p )
    {
      XYZ pt = p;
      if( null != _transformations )
      {
        int n = _transformations.Count;
        for( int i = n - 1; i >= 0; --i )
        {
          pt = _transformations[ i ].OfPoint( pt );
        }
      }
      return pt;
    }
    #endregion // Transform stack

    #region Store vertices, lines and triangles
    int VertexIndexOf( XYZ p )
    {
      return _vertices.Add( new IntPoint3d( p ) );
    }

    void DrawLine( XYZ p, XYZ q )
    {
      _lines.Add( new LineSegmentIndices(
        VertexIndexOf( TransformPoint( p ) ),
        VertexIndexOf( TransformPoint( q ) ) ) );
    }

    void DrawInstanceTriangle( XYZ p, XYZ q, XYZ r )
    {
      _instance_triangles.Add( new TriangleIndices(
        VertexIndexOf( TransformPoint( p ) ),
        VertexIndexOf( TransformPoint( q ) ),
        VertexIndexOf( TransformPoint( r ) ) ) );
    }

    void DrawSymbolTriangle( XYZ p, XYZ q, XYZ r )
    {
      Debug.Assert( 1 == _transformations.Count,
        "expected single level of symbol transformations" );

      if( null == _symbol_transform )
      {
        _symbol_transform = _transformations[ 0 ];
      }
      else
      {
        Debug.Assert( _symbol_transform.AlmostEqual(
          _transformations[ 0 ] ) );
      }

      _symbol_triangles.Add( new TriangleIndices(
        VertexIndexOf( p ),
        VertexIndexOf( q ),
        VertexIndexOf( r ) ) );
    }
    #endregion // Store vertices, lines and triangles

    public TriangleCollector()
    {
      _vertices = new IntVertexLookup();
      _lines = new List<LineSegmentIndices>();
      _instance_triangles = new List<TriangleIndices>();
      _symbol_transform = null;
      _symbol_triangles = new List<TriangleIndices>();
      _transformations = null;
    }

    /// <summary>
    /// Get geometry triangles from an element
    /// </summary>
    public void DrawElement( Element e )
    {
      // If it is a Group, look at its components

      if( e is Group )
      {
        IList<ElementId> ids = (e as Group).GetMemberIds();
        foreach( ElementId id in ids )
        {
          DrawElement( e.Document.GetElement( id ) );
        }
      }

      Options opt = new Options();
      GeometryElement geo = e.get_Geometry( opt );
      DrawGeometry( geo );
    }

    /// <summary>
    /// Get geometry triangles from a geometry element
    /// </summary>
    void DrawGeometry( GeometryElement geo )
    {
      if( null == geo )
      {
        return;
      }
      foreach( GeometryObject obj in geo )
      {
        if( obj is Curve )
        {
          DrawCurve( obj as Curve );
        }
        else if( obj is GeometryInstance )
        {
          DrawInstance( obj as GeometryInstance );
        }
        else if( obj is Mesh )
        {
          DrawMesh( obj as Mesh );
        }
        else if( obj is PolyLine )
        {
          DrawPolyLine( obj as PolyLine );
        }
        else if( obj is Solid )
        {
          DrawSolid( obj as Solid );
        }
      }
    }

    void DrawCurve( Curve c )
    {
      DrawLines( c.Tessellate() );
    }

    void DrawPolyLine( PolyLine p )
    {
      DrawLines( p.GetCoordinates() );
    }

    void DrawLines( IList<XYZ> pts )
    {
      XYZ p = null;
      foreach( XYZ q in pts )
      {
        if( null != p )
        {
          DrawLine( p, q );
        }
        p = q;
      }
    }

    void DrawInstance( GeometryInstance inst )
    {
      GeometryElement symbol_geo = inst.SymbolGeometry;

      if( null != symbol_geo )
      {
        PushTransformation( inst.Transform );
        DrawGeometry( symbol_geo );
        PopTransformation();
      }
    }

    void DrawMesh( Mesh mesh )
    {
      int n = mesh.NumTriangles;
      for( int i = 0; i < n; ++i )
      {
        MeshTriangle t = mesh.get_Triangle( i );
        XYZ p = t.get_Vertex( 0 );
        XYZ q = t.get_Vertex( 1 );
        XYZ r = t.get_Vertex( 2 );
        DrawInstanceTriangle( p, q, r );
        DrawSymbolTriangle( p, q, r );
      }
    }

    void DrawSolid( Solid solid )
    {
      foreach( Face f in solid.Faces )
      {
        DrawFace( f );
      }
      foreach( Edge e in solid.Edges )
      {
        DrawEdge( e );
      }
    }

    void DrawEdge( Edge e )
    {
      DrawLines( e.Tessellate() );
    }

    void DrawFace( Face f )
    {
      DrawMesh( f.Triangulate() );
    }

    #region ElementViewer VB.NET code
#if ElementViewer

    Private Sub PushTransformation(ByVal transform As Autodesk.Revit.DB.Transform)
        If (mTransformations Is Nothing) Then
            mTransformations = New System.Collections.Generic.List(Of Autodesk.Revit.DB.Transform)
        End If
        mTransformations.Add(transform)
    End Sub

    Private Sub PopTransformation()

        If (mTransformations Is Nothing) Then
            Exit Sub
        End If

        If (mTransformations.Count = 1) Then
            mTransformations = Nothing
            Exit Sub
        End If

        Dim newTransformations As System.Collections.Generic.List(Of Autodesk.Revit.DB.Transform)
        newTransformations = New System.Collections.Generic.List(Of Autodesk.Revit.DB.Transform)
        Dim i As Integer
        For i = 0 To mTransformations.Count - 2

            newTransformations.Add(mTransformations.Item(i))

        Next

        mTransformations = newTransformations

    End Sub


    ' Note: Some element does not expose geometry, for example, curtain wall and dimension.
    ' In case of a curtain wall, try selecting a whole wall by a window/box instead of a single pick. 
    ' It will then select internal components and be able to display its geometry.
    ' 
    Private Sub DrawElement(ByVal elem As Autodesk.Revit.DB.Element)

        ' if it is a Group. we will need to look at its components. 
        If TypeOf elem Is Autodesk.Revit.DB.Group Then

            Dim group As Autodesk.Revit.DB.Group = elem
            Dim members As Autodesk.Revit.DB.ElementArray = group.GetMemberIds()

            Dim elm As Autodesk.Revit.DB.ElementId
            For Each elm In members
                DrawElement(group.Document.GetElement(elm))
            Next

        Else

            ' not a group. look at the geom data. 
            Dim geom As Autodesk.Revit.DB.GeometryElement = elem.Geometry(mOptions)
            If Not (geom Is Nothing) Then
                DrawElement(geom)
            End If

        End If

    End Sub

    ''' <summary>
    ''' Draw geometry of element.
    ''' </summary>
    ''' <param name="elementGeom"></param>
    ''' <remarks></remarks>
    Private Sub DrawElement(ByVal elementGeom As Autodesk.Revit.DB.GeometryElement)

        If elementGeom Is Nothing Then
            Exit Sub
        End If

        Dim geomObject As Autodesk.Revit.DB.GeometryObject

        Dim Objects As IEnumerator(Of GeometryObject) = elementGeom.GetEnumerator()

        'For Each geomObject In elementGeom.Objects
        While Objects.MoveNext

            geomObject = Objects.Current

            If (TypeOf geomObject Is Autodesk.Revit.DB.Curve) Then
                DrawCurve(geomObject)
            ElseIf (TypeOf geomObject Is Autodesk.Revit.DB.GeometryInstance) Then
                DrawInstance(geomObject)
            ElseIf (TypeOf geomObject Is Autodesk.Revit.DB.Mesh) Then
                DrawMesh(geomObject)
            ElseIf (TypeOf geomObject Is Autodesk.Revit.DB.Solid) Then
                DrawSolid(geomObject)
            ElseIf (TypeOf geomObject Is Autodesk.Revit.DB.PolyLine) Then
                DrawPoly(geomObject)
            End If

        End While

    End Sub

    ''' <summary>
    ''' add the primitive line to the Wireframe object.
    ''' </summary>
    ''' <param name="startPoint"></param>
    ''' <param name="endPoint"></param>
    ''' <remarks></remarks>
    Private Sub ViewerDrawLine(ByRef startPoint As Autodesk.Revit.DB.XYZ, ByRef endPoint As Autodesk.Revit.DB.XYZ)

        If (mViewer Is Nothing) Then
            Exit Sub
        End If

        Dim transformedStart As Autodesk.Revit.DB.XYZ
        transformedStart = startPoint
        Dim transformedEnd As Autodesk.Revit.DB.XYZ
        transformedEnd = endPoint

        If Not (mTransformations Is Nothing) Then
            Dim count As Long = mTransformations.Count
            Dim index As Long
            For index = count - 1 To 0 Step -1
                transformedStart = TransformPoint(mTransformations(index), transformedStart)
                transformedEnd = TransformPoint(mTransformations(index), transformedEnd)
            Next
        End If

        mViewer.Add(transformedStart.X, transformedStart.Y, transformedStart.Z, transformedEnd.X, transformedEnd.Y, transformedEnd.Z)

    End Sub

    ''' <summary>
    ''' transform point to be fit.
    ''' </summary>
    ''' <param name="transform"></param>
    ''' <param name="point"></param>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Function TransformPoint(ByVal transform As Autodesk.Revit.DB.Transform, ByRef point As Autodesk.Revit.DB.XYZ) As Autodesk.Revit.DB.XYZ

        Return transform.OfPoint(point)

        'Dim result As New Autodesk.Revit.DB.XYZ

        'Dim i As Integer
        'For i = 0 To 2

        '    result.Item(i) = transform.Origin.Item(i)
        '    Dim j As Integer
        '    For j = 0 To 2
        '        result.Item(i) = result.Item(i) + (transform.Basis(j).Item(i) * point.Item(j))
        '    Next

        'Next

        'Return result

    End Function

    ''' <summary>
    ''' add the primitive curve to the Wireframe object.
    ''' </summary>
    ''' <param name="geomCurve"></param>
    ''' <remarks></remarks>
    Private Sub DrawCurve(ByVal geomCurve As Autodesk.Revit.DB.Curve)

        DrawPoints(geomCurve.Tessellate)

    End Sub

    ''' <summary>
    ''' add the poly line to the Wireframe object.
    ''' </summary>
    ''' <param name="polyLine"></param>
    ''' <remarks></remarks>
    Private Sub DrawPoly(ByVal polyLine As Autodesk.Revit.DB.PolyLine)

        DrawPoints(polyLine.GetCoordinates())

    End Sub

    ''' <summary>
    ''' add the primitive points to the Wireframe object.
    ''' </summary>
    ''' <param name="points"></param>
    ''' <remarks></remarks>
    Private Sub DrawPoints(ByVal points As List(Of Autodesk.Revit.DB.XYZ))

        Dim previousPoint As Autodesk.Revit.DB.XYZ
        previousPoint = points.Item(0)
        Dim point As Autodesk.Revit.DB.XYZ

        Dim i As Integer
        For i = 0 To points.Count - 1

            point = points.Item(i)
            If (i <> 0) Then
                ViewerDrawLine(previousPoint, point)
            End If

            previousPoint = point

        Next

    End Sub

    ''' <summary>
    ''' add the primitive instance to the Wireframe object.
    ''' </summary>
    ''' <param name="geomInstance"></param>
    ''' <remarks></remarks>
    Private Sub DrawInstance(ByVal geomInstance As Autodesk.Revit.DB.GeometryInstance)

        PushTransformation(geomInstance.Transform)

        Dim geomSymbol As Autodesk.Revit.DB.GeometryElement
        geomSymbol = geomInstance.SymbolGeometry

        If Not (geomSymbol Is Nothing) Then
            DrawElement(geomSymbol)
        End If

        PopTransformation()

    End Sub

    ''' <summary>
    ''' add the primitive mesh to the Wireframe object.
    ''' </summary>
    ''' <param name="geomMesh"></param>
    ''' <remarks></remarks>
    Private Sub DrawMesh(ByVal geomMesh As Autodesk.Revit.DB.Mesh)

        Dim i As Integer

        For i = 0 To geomMesh.NumTriangles - 1
            Dim triangle As Autodesk.Revit.DB.MeshTriangle
            triangle = geomMesh.Triangle(i)

            ViewerDrawLine(triangle.Vertex(0), triangle.Vertex(1))
            ViewerDrawLine(triangle.Vertex(1), triangle.Vertex(2))
            ViewerDrawLine(triangle.Vertex(2), triangle.Vertex(0))

        Next

    End Sub

    ''' <summary>
    ''' add the primitive solid to the Wireframe object.
    ''' </summary>
    ''' <param name="geomSolid"></param>
    ''' <remarks></remarks>
    Private Sub DrawSolid(ByVal geomSolid As Autodesk.Revit.DB.Solid)

        Dim face As Autodesk.Revit.DB.Face
        For Each face In geomSolid.Faces
            DrawFace(face)
        Next

        Dim Edge As Autodesk.Revit.DB.Edge
        For Each Edge In geomSolid.Edges
            DrawEdge(Edge)
        Next

    End Sub

    ''' <summary>
    ''' add the primitive edge to the Wireframe object.
    ''' </summary>
    ''' <param name="geomEdge"></param>
    ''' <remarks></remarks>
    Private Sub DrawEdge(ByVal geomEdge As Autodesk.Revit.DB.Edge)

        DrawPoints(geomEdge.Tessellate)

    End Sub

    ''' <summary>
    ''' add the primitive face to the Wireframe object.
    ''' </summary>
    ''' <param name="geomFace"></param>
    ''' <remarks></remarks>
    Private Sub DrawFace(ByVal geomFace As Autodesk.Revit.DB.Face)

        DrawMesh(geomFace.Triangulate)

    End Sub

#endif // ElementViewer
    #endregion // ElementViewer VB.NET code

    public string VertexCoordinates
    {
      get
      {
        return _vertices.Coordinates;
      }
    }

    public string InstanceTriangleIndices
    {
      get
      {
        return string.Join( " ",
          _instance_triangles.Select(
            t => t.ToString() ) );
      }
    }

    public string SymbolTriangleIndices
    {
      get
      {
        return string.Join( " ",
          _symbol_triangles.Select(
            t => t.ToString() ) );
      }
    }

    public string SymbolTransform
    {
      get
      { 
        Transform t = _symbol_transform;
        IntPoint3d origin = new IntPoint3d( t.Origin );
        return Util.PointString( t.BasisX, true )
          + " " + Util.PointString( t.BasisY, true )
          + " " + Util.PointString( t.BasisX, true )
          + " " + origin.ToString( true );
      }
    }
  }
}
