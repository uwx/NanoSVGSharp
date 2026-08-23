using System.Globalization;
using System.Runtime.CompilerServices;

namespace NanoSvgSharp;

public enum NSVGpaintType {
    NSVG_PAINT_UNDEF = -1,
    NSVG_PAINT_NONE = 0,
    NSVG_PAINT_COLOR = 1,
    NSVG_PAINT_LINEAR_GRADIENT = 2,
    NSVG_PAINT_RADIAL_GRADIENT = 3
};

public enum NSVGspreadType {
    NSVG_SPREAD_PAD = 0,
    NSVG_SPREAD_REFLECT = 1,
    NSVG_SPREAD_REPEAT = 2
};

public enum NSVGlineJoin {
    NSVG_JOIN_MITER = 0,
    NSVG_JOIN_ROUND = 1,
    NSVG_JOIN_BEVEL = 2
};

public enum NSVGlineCap {
    NSVG_CAP_BUTT = 0,
    NSVG_CAP_ROUND = 1,
    NSVG_CAP_SQUARE = 2
};

public enum NSVGfillRule {
    NSVG_FILLRULE_NONZERO = 0,
    NSVG_FILLRULE_EVENODD = 1
};

public enum NSVGflags {
    NSVG_FLAGS_VISIBLE = 0x01
};

public enum NSVGpaintOrder {
    NSVG_PAINT_FILL = 0x00,
    NSVG_PAINT_MARKERS = 0x01,
    NSVG_PAINT_STROKE = 0x02,
};

public struct NSVGgradientStop {
    public uint color;
    public float offset;
};

public struct NSVGgradient() {
    public InlineArray6<float> xform;
    public sbyte spread;
    public float fx, fy;
    public int nstops;
    public NSVGgradientStop[] stops = [];
};

public struct NSVGpaint {
    public sbyte type;
    // union {
    public uint color;
    public NSVGgradient? gradient; // null = no gradient
    // };
}

public struct NSVGpath()
{
    public float[] pts = [];					// Cubic bezier points: x0,y0, [cpx1,cpx1,cpx2,cpy2,x1,y1], ...
    public int npts;					// Total number of bezier points.
    public sbyte closed;				// Flag indicating if shapes should be treated as closed.
    public InlineArray4<float> bounds;			// Tight bounding box of the shape [minx,miny,maxx,maxy].
}

public struct NSVGshape()
{
    public string id = "";			// Optional 'id' attr of the shape or its group
    public NSVGpaint fill;				// Fill paint
    public NSVGpaint stroke;			// Stroke paint
    public float opacity;				// Opacity of the shape.
    public float strokeWidth;			// Stroke width (scaled).
    public float strokeDashOffset;		// Stroke dash offset (scaled).
    public InlineArray8<float> strokeDashArray;	// Stroke dash array (scaled).
    public sbyte strokeDashCount;		// Number of dash values in dash array.
    public sbyte strokeLineJoin;		// Stroke join type.
    public sbyte strokeLineCap;			// Stroke cap type.
    public float miterLimit;			// Miter limit
    public sbyte fillRule;				// Fill rule, see NSVGfillRule.
    public byte paintOrder;	// Encoded paint order (3×2-bit fields) see NSVGpaintOrder
    public byte flags;		// Logical or of NSVG_FLAGS_* flags
    public InlineArray4<float> bounds;			// Tight bounding box of the shape [minx,miny,maxx,maxy].
    public string fillGradient = "";	// Optional 'id' of fill gradient
    public string strokeGradient = "";	// Optional 'id' of stroke gradient
    public InlineArray6<float> xform;				// Root transformation for fill/stroke gradient
    public List<NSVGpath> paths = [];			// Linked list of paths in the image.
}

public struct NSVGimage()
{
    public float width;				// Width of the image.
    public float height;				// Height of the image.
    public List<NSVGshape> shapes = [];			// Linked list of shapes in the image.
}

public class NSVG
{
    private const float NSVG_PI = (3.14159265358979323846264338327f);
    private const float NSVG_KAPPA90 = (0.5522847493f);	// Length proportional to radius of a cubic bezier handle for 90deg arcs.

    private const byte NSVG_ALIGN_MIN = 0;
    private const byte NSVG_ALIGN_MID = 1;
    private const byte NSVG_ALIGN_MAX = 2;
    private const byte NSVG_ALIGN_NONE = 0;
    private const byte NSVG_ALIGN_MEET = 1;
    private const byte NSVG_ALIGN_SLICE = 2;

    private static uint NSVG_RGB(byte r, byte g, byte b) => (((uint)r) | ((uint)g << 8) | ((uint)b << 16));
    
    static bool nsvg__isspace(char c)
    {
        return c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
    }

    static bool nsvg__isdigit(char c)
    {
        return c >= '0' && c <= '9';
    }
    
    static float nsvg__minf(float a, float b) { return a < b ? a : b; }
    static float nsvg__maxf(float a, float b) { return a > b ? a : b; }
    
    // Simple XML parser

    private const int NSVG_XML_TAG = 1;
    private const int NSVG_XML_CONTENT = 2;
    private const int NSVG_XML_MAX_ATTRIBS = 256;

    static void nsvg__parseContent(
        Span<char> s,
        Action<object, ReadOnlySpan<char>> contentCb,
        object ud)
    {
        // Trim start white spaces
        while (s.Length > 0 && nsvg__isspace(s[0]))
        {
            s = s[1..];
        }

        if (s.Length == 0) return;
        contentCb(ud, s);
    }
    
    // Trims a span at its first '\0' (or returns it unchanged if none is present).
    static Span<char> nsvg__trimToNull(Span<char> s)
    {
        int i = s.IndexOf('\0');
        return i < 0 ? s : s[..i];
    }
    
    static void nsvg__parseElement(Span<char> s,
        Action<object, ReadOnlySpan<char>, ReadOnlySpan<string>>? startelCb,
        Action<object, ReadOnlySpan<char>>? endelCb,
        object ud)
    {
        var attr = new string[NSVG_XML_MAX_ATTRIBS];
        int nattr = 0;
        Span<char> name;
        bool start = false;
        bool end = false;
        char quote;

        // Skip white space after the '<'
        while (s.Length > 0 && nsvg__isspace(s[0]))
        {
            s = s[1..];
        }

        // Check if the tag is end tag
        if (s.Length > 0 && s[0] == '/') {
            s = s[1..];
            end = true;
        } else {
            start = true;
        }

        // Skip comments, data and preprocessor stuff.
        if (s.Length == 0 || s[0] == '?' || s[0] == '!')
            return;

        // Get tag name
        name = s;
        while (s.Length > 0 && !nsvg__isspace(s[0])) s = s[1..];
        if (s.Length > 0) { s[0] = '\0'; s = s[1..]; } // add \0 to tag name
        // trim name to \0
        name = nsvg__trimToNull(name);

        // Get attribs
        while (!end && s.Length > 0 && nattr < NSVG_XML_MAX_ATTRIBS-3) {
            Span<char> aname;
            Span<char> value;

            // Skip white space before the attrib name
            while (s.Length > 0 && nsvg__isspace(s[0])) s = s[1..];
            if (s.Length == 0) break;
            if (s[0] == '/') {
                end = true;
                break;
            }
            aname = s;
            // Find end of the attrib name.
            while (s.Length > 0 && !nsvg__isspace(s[0]) && s[0] != '=') s = s[1..];
            if (s.Length > 0) { s[0] = '\0'; s = s[1..]; } // add \0 to attrib name
            // trim name to \0
            aname = nsvg__trimToNull(aname);
            
            // Skip until the beginning of the value.
            while (s.Length > 0 && s[0] != '\"' && s[0] != '\'') s = s[1..];
            if (s.Length == 0) break;
            quote = s[0];
            s = s[1..];
            // Store value and find the end of it.
            value = s;
            while (s.Length > 0 && s[0] != quote) s = s[1..];
            if (s.Length > 0) { s[0] = '\0'; s = s[1..]; } // add \0 to attrib value
            // trim value to \0
            value = nsvg__trimToNull(value);

            // Store only well formed attributes
            if (aname.Length > 0 && value.Length > 0) {
                attr[nattr++] = new string(aname);
                attr[nattr++] = new string(value);
            }
        }

        // List terminator
        // attr[nattr++] = 0;
        // attr[nattr++] = 0;

        // Call callbacks.
        if (start && startelCb != null)
            startelCb(ud, name, attr.AsSpan(..nattr));
        if (end && endelCb != null)
            endelCb(ud, name);
    }


    static int nsvg__parseXML(Span<char> input,
        Action<object, ReadOnlySpan<char>, ReadOnlySpan<string>>? startelCb,
        Action<object, ReadOnlySpan<char>>? endelCb,
        Action<object, ReadOnlySpan<char>> contentCb,
        object ud)
    {
        Span<char> s = input;
        Span<char> mark = s;
        int state = NSVG_XML_CONTENT;
        while (s.Length > 0) {
            if (s[0] == '<' && state == NSVG_XML_CONTENT) {
                // Start of a tag
                s[0] = '\0'; s = s[1..];
                nsvg__parseContent(mark[..mark.IndexOf('\0')], contentCb, ud);
                mark = s;
                state = NSVG_XML_TAG;
            } else if (s[0] == '>' && state == NSVG_XML_TAG) {
                // Start of a content or new tag.
                s[0] = '\0'; s = s[1..];
                nsvg__parseElement(mark[..mark.IndexOf('\0')], startelCb, endelCb, ud);
                mark = s;
                state = NSVG_XML_CONTENT;
            } else {
                s = s[1..];
            }
        }

        return 1;
    }

	/* Simple SVG parser. */

	private const byte NSVG_MAX_ATTR = 128;

	enum NSVGgradientUnits {
		NSVG_USER_SPACE = 0,
		NSVG_OBJECT_SPACE = 1
	};

	private const byte NSVG_MAX_DASHES = 8;
	private const byte NSVG_MAX_CLASSES = 32;

	enum NSVGunits {
		NSVG_UNITS_USER,
		NSVG_UNITS_PX,
		NSVG_UNITS_PT,
		NSVG_UNITS_PC,
		NSVG_UNITS_MM,
		NSVG_UNITS_CM,
		NSVG_UNITS_IN,
		NSVG_UNITS_PERCENT,
		NSVG_UNITS_EM,
		NSVG_UNITS_EX
	};

	struct NSVGcoordinate {
		public float value;
		public NSVGunits units;
	};

	struct NSVGlinearData {
		public NSVGcoordinate x1, y1, x2, y2;
	};

	struct NSVGradialData {
		public NSVGcoordinate cx, cy, r, fx, fy;
	};

	class NSVGgradientData() // converted to class because gets frequently taken as ref
	{
		public string id = "";
		public string @ref = "";
		public sbyte type;
		// union {
			public NSVGlinearData linear;
			public NSVGradialData radial;
		// };
		public sbyte spread;
		public sbyte units;
		public InlineArray6<float> xform;
		public int nstops;
		public List<NSVGgradientStop> stops = [];
	}

	struct NSVGattrib
	{
		public string id;
		public InlineArray6<float> xform;
		public uint fillColor;
		public uint strokeColor;
		public float opacity;
		public float fillOpacity;
		public float strokeOpacity;
		public string fillGradient;
		public string strokeGradient;
		public float strokeWidth;
		public float strokeDashOffset;
		public InlineArray8<float> strokeDashArray;
		public int strokeDashCount;
		public NSVGlineJoin strokeLineJoin;
		public NSVGlineCap strokeLineCap;
		public float miterLimit;
		public NSVGfillRule fillRule;
		public float fontSize;
		public uint stopColor;
		public float stopOpacity;
		public float stopOffset;
		public sbyte hasFill;
		public sbyte hasStroke;
		public sbyte visible;
	    public byte paintOrder;
	};

	struct NSVGstyleDeclaration
	{
		public string className;
		public string propertiesText;
	}

	class NSVGparser()
	{
		public InlineArray128<NSVGattrib> attr;
		public int attrHead;
		public List<float> pts = [];
		// cpts: removed, use pts.Count instead (note that cpts is num of vectors not num of floats, so *2)
		public int npts;
		public List<NSVGpath> plist = [];
		public NSVGimage image;
		public List<NSVGstyleDeclaration> styles = [];
		public List<NSVGgradientData> gradients = [];
		public float viewMinx, viewMiny, viewWidth, viewHeight;
		public int alignX, alignY, alignType;
		public float dpi;
		public byte pathFlag;
		public byte defsFlag;
		public byte styleFlag;
	}
	
	static void nsvg__xformIdentity(ref InlineArray6<float> t)
	{
		t[0] = 1.0f; t[1] = 0.0f;
		t[2] = 0.0f; t[3] = 1.0f;
		t[4] = 0.0f; t[5] = 0.0f;
	}

	static void nsvg__xformSetTranslation(ref InlineArray6<float> t, float tx, float ty)
	{
		t[0] = 1.0f; t[1] = 0.0f;
		t[2] = 0.0f; t[3] = 1.0f;
		t[4] = tx; t[5] = ty;
	}

	static void nsvg__xformSetScale(ref InlineArray6<float> t, float sx, float sy)
	{
		t[0] = sx; t[1] = 0.0f;
		t[2] = 0.0f; t[3] = sy;
		t[4] = 0.0f; t[5] = 0.0f;
	}

	static void nsvg__xformSetSkewX(ref InlineArray6<float> t, float a)
	{
		t[0] = 1.0f; t[1] = 0.0f;
		t[2] = MathF.Tan(a); t[3] = 1.0f;
		t[4] = 0.0f; t[5] = 0.0f;
	}

	static void nsvg__xformSetSkewY(ref InlineArray6<float> t, float a)
	{
		t[0] = 1.0f; t[1] = MathF.Tan(a);
		t[2] = 0.0f; t[3] = 1.0f;
		t[4] = 0.0f; t[5] = 0.0f;
	}

	static void nsvg__xformSetRotation(ref InlineArray6<float> t, float a)
	{
		float cs = MathF.Cos(a), sn = MathF.Sin(a);
		t[0] = cs; t[1] = sn;
		t[2] = -sn; t[3] = cs;
		t[4] = 0.0f; t[5] = 0.0f;
	}

	static void nsvg__xformMultiply(ref InlineArray6<float> t, ref readonly InlineArray6<float> s)
	{
		float t0 = t[0] * s[0] + t[1] * s[2];
		float t2 = t[2] * s[0] + t[3] * s[2];
		float t4 = t[4] * s[0] + t[5] * s[2] + s[4];
		t[1] = t[0] * s[1] + t[1] * s[3];
		t[3] = t[2] * s[1] + t[3] * s[3];
		t[5] = t[4] * s[1] + t[5] * s[3] + s[5];
		t[0] = t0;
		t[2] = t2;
		t[4] = t4;
	}

	static void nsvg__xformInverse(ref InlineArray6<float> inv, ref InlineArray6<float> t)
	{
		double invdet, det = (double)t[0] * t[3] - (double)t[2] * t[1];
		if (det > -1e-6 && det < 1e-6) {
			nsvg__xformIdentity(ref t);
			return;
		}
		invdet = 1.0 / det;
		inv[0] = (float)(t[3] * invdet);
		inv[2] = (float)(-t[2] * invdet);
		inv[4] = (float)(((double)t[2] * t[5] - (double)t[3] * t[4]) * invdet);
		inv[1] = (float)(-t[1] * invdet);
		inv[3] = (float)(t[0] * invdet);
		inv[5] = (float)(((double)t[1] * t[4] - (double)t[0] * t[5]) * invdet);
	}

	static void nsvg__xformPremultiply(ref InlineArray6<float> t, ref InlineArray6<float> s)
	{
		InlineArray6<float> s2 = default;
		memcpy<float>(s2, s, 6);
		nsvg__xformMultiply(ref s2, in t);
		memcpy<float>(t, s2, 6);
	}

	private static void memcpy<T>(Span<T> dest, Span<T> src, nint count)
	{
		for (var i = 0; i < count; i++)
		{
			dest[i] = src[i];
		}
	}
	
	static void nsvg__xformPoint(ref float dx, ref float dy, float x, float y, InlineArray6<float> t)
	{
		dx = x*t[0] + y*t[2] + t[4];
		dy = x*t[1] + y*t[3] + t[5];
	}

	static void nsvg__xformVec(ref float dx, ref float dy, float x, float y, InlineArray6<float> t)
	{
		dx = x*t[0] + y*t[2];
		dy = x*t[1] + y*t[3];
	}

	private const float NSVG_EPSILON = (1e-12f);
	
	static bool nsvg__ptInBounds(ReadOnlySpan<float> pt, InlineArray4<float> bounds) // len 2, 4
	{
		return pt[0] >= bounds[0] && pt[0] <= bounds[2] && pt[1] >= bounds[1] && pt[1] <= bounds[3];
	}
	
	static double nsvg__evalBezier(double t, double p0, double p1, double p2, double p3)
	{
		double it = 1.0-t;
		return it*it*it*p0 + 3.0*it*it*t*p1 + 3.0*it*t*t*p2 + t*t*t*p3;
	}

	static void nsvg__curveBounds(ref InlineArray4<float> bounds, ReadOnlySpan<float> curve)
	{
		int i, j, count;
		InlineArray2<double> roots = default;
		double a, b, c, b2ac, t, v;
		ReadOnlySpan<float> v0 = curve[0..];
		ReadOnlySpan<float> v1 = curve[2..];
		ReadOnlySpan<float> v2 = curve[4..];
		ReadOnlySpan<float> v3 = curve[6..];

		// Start the bounding box by end points
		bounds[0] = nsvg__minf(v0[0], v3[0]);
		bounds[1] = nsvg__minf(v0[1], v3[1]);
		bounds[2] = nsvg__maxf(v0[0], v3[0]);
		bounds[3] = nsvg__maxf(v0[1], v3[1]);

		// Bezier curve fits inside the convex hull of it's control points.
		// If control points are inside the bounds, we're done.
		if (nsvg__ptInBounds(v1, bounds) && nsvg__ptInBounds(v2, bounds))
			return;

		// Add bezier curve inflection points in X and Y.
		for (i = 0; i < 2; i++) {
			a = -3.0 * v0[i] + 9.0 * v1[i] - 9.0 * v2[i] + 3.0 * v3[i];
			b = 6.0 * v0[i] - 12.0 * v1[i] + 6.0 * v2[i];
			c = 3.0 * v1[i] - 3.0 * v0[i];
			count = 0;
			if (Math.Abs(a) < NSVG_EPSILON) {
				if (Math.Abs(b) > NSVG_EPSILON) {
					t = -c / b;
					if (t > NSVG_EPSILON && t < 1.0-NSVG_EPSILON)
						roots[count++] = t;
				}
			} else {
				b2ac = b*b - 4.0*c*a;
				if (b2ac > NSVG_EPSILON) {
					t = (-b + Math.Sqrt(b2ac)) / (2.0 * a);
					if (t > NSVG_EPSILON && t < 1.0-NSVG_EPSILON)
						roots[count++] = t;
					t = (-b - Math.Sqrt(b2ac)) / (2.0 * a);
					if (t > NSVG_EPSILON && t < 1.0-NSVG_EPSILON)
						roots[count++] = t;
				}
			}
			for (j = 0; j < count; j++) {
				v = nsvg__evalBezier(roots[j], v0[i], v1[i], v2[i], v3[i]);
				bounds[0+i] = nsvg__minf(bounds[0+i], (float)v);
				bounds[2+i] = nsvg__maxf(bounds[2+i], (float)v);
			}
		}
	}

	static byte nsvg__encodePaintOrder(NSVGpaintOrder a, NSVGpaintOrder b, NSVGpaintOrder c) {
		return (byte)(((int)a & 0x03) | (((int)b & 0x03) << 2) | (((int)c & 0x03) << 4));
	}
	
	static NSVGparser nsvg__createParser()
	{
		var p = new NSVGparser();
		p.image = new NSVGimage();
		
		// Init style
		nsvg__xformIdentity(ref p.attr[0].xform);
		p.attr[0].id = "";
		p.attr[0].fillColor = NSVG_RGB(0,0,0);
		p.attr[0].strokeColor = NSVG_RGB(0,0,0);
		p.attr[0].opacity = 1;
		p.attr[0].fillOpacity = 1;
		p.attr[0].strokeOpacity = 1;
		p.attr[0].stopOpacity = 1;
		p.attr[0].strokeWidth = 1;
		p.attr[0].strokeLineJoin = NSVGlineJoin.NSVG_JOIN_MITER;
		p.attr[0].strokeLineCap = NSVGlineCap.NSVG_CAP_BUTT;
		p.attr[0].miterLimit = 4;
		p.attr[0].fillRule = NSVGfillRule.NSVG_FILLRULE_NONZERO;
		p.attr[0].hasFill = 1;
		p.attr[0].visible = 1;
		p.attr[0].paintOrder = nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_MARKERS);

		return p;
	}

	static void nsvg__resetPath(NSVGparser p)
	{
		p.npts = 0;
	}
	
	static void nsvg__addPoint(NSVGparser p, float x, float y)
	{
		if ((p.npts+1)*2 > p.pts.Count) {
			p.pts.AddRange((ReadOnlySpan<float>)[0, 0]);
		}
		p.pts[p.npts*2+0] = x;
		p.pts[p.npts*2+1] = y;
		p.npts++;
	}
	
	static void nsvg__moveTo(NSVGparser p, float x, float y)
	{
		if (p.npts > 0) {
			p.pts[(p.npts-1)*2+0] = x;
			p.pts[(p.npts-1)*2+1] = y;
		} else {
			nsvg__addPoint(p, x, y);
		}
	}

	static void nsvg__lineTo(NSVGparser p, float x, float y)
	{
		float px,py, dx,dy;
		if (p.npts > 0) {
			px = p.pts[(p.npts-1)*2+0];
			py = p.pts[(p.npts-1)*2+1];
			dx = x - px;
			dy = y - py;
			nsvg__addPoint(p, px + dx/3.0f, py + dy/3.0f);
			nsvg__addPoint(p, x - dx/3.0f, y - dy/3.0f);
			nsvg__addPoint(p, x, y);
		}
	}
	
	static void nsvg__cubicBezTo(NSVGparser p, float cpx1, float cpy1, float cpx2, float cpy2, float x, float y)
	{
		if (p.npts > 0) {
			nsvg__addPoint(p, cpx1, cpy1);
			nsvg__addPoint(p, cpx2, cpy2);
			nsvg__addPoint(p, x, y);
		}
	}

	static ref NSVGattrib nsvg__getAttr(NSVGparser p)
	{
		return ref p.attr[p.attrHead];
	}

	static void nsvg__pushAttr(NSVGparser p)
	{
		if (p.attrHead < NSVG_MAX_ATTR-1) {
			p.attrHead++;
			p.attr[p.attrHead] = p.attr[p.attrHead - 1];
		}
	}
	
	static void nsvg__popAttr(NSVGparser p)
	{
		if (p.attrHead > 0)
			p.attrHead--;
	}
	
	static float nsvg__actualOrigX(NSVGparser p)
	{
		return p.viewMinx;
	}

	static float nsvg__actualOrigY(NSVGparser p)
	{
		return p.viewMiny;
	}

	static float nsvg__actualWidth(NSVGparser p)
	{
		return p.viewWidth;
	}

	static float nsvg__actualHeight(NSVGparser p)
	{
		return p.viewHeight;
	}

	static float nsvg__actualLength(NSVGparser p)
	{
		float w = nsvg__actualWidth(p), h = nsvg__actualHeight(p);
		return MathF.Sqrt(w*w + h*h) / MathF.Sqrt(2.0f);
	}

	static float nsvg__convertToPixels(NSVGparser p, NSVGcoordinate c, float orig, float length)
	{
		ref NSVGattrib attr = ref nsvg__getAttr(p);
		switch (c.units) {
			case NSVGunits.NSVG_UNITS_USER:		return c.value;
			case NSVGunits.NSVG_UNITS_PX:			return c.value;
			case NSVGunits.NSVG_UNITS_PT:			return c.value / 72.0f * p.dpi;
			case NSVGunits.NSVG_UNITS_PC:			return c.value / 6.0f * p.dpi;
			case NSVGunits.NSVG_UNITS_MM:			return c.value / 25.4f * p.dpi;
			case NSVGunits.NSVG_UNITS_CM:			return c.value / 2.54f * p.dpi;
			case NSVGunits.NSVG_UNITS_IN:			return c.value * p.dpi;
			case NSVGunits.NSVG_UNITS_EM:			return c.value * attr.fontSize;
			case NSVGunits.NSVG_UNITS_EX:			return c.value * attr.fontSize * 0.52f; // x-height of Helvetica.
			case NSVGunits.NSVG_UNITS_PERCENT:	return orig + c.value / 100.0f * length;
			default:					return c.value;
		}
	}


	private static NSVGgradientData? nsvg__findGradientData(NSVGparser p, string id)
	{
		for (var i = 0; i < p.gradients.Count; i++)
		{
			if (p.gradients[i].id == id)
			{
				return p.gradients[i];
			}
		}

		return null;
	}

	// We roll our own string to float because the std library one uses locale and messes things up.
	// The C# version uses InvariantCulture which is equally locale-independent.
	static double nsvg__atof(ReadOnlySpan<char> s)
	{
		if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var res))
			return res;
		return 0.0;
	}

	// Copies a number literal (sign, int, fraction, exponent) out of 's' into 'literal'.
	// Returns the number of chars consumed from 's'.
	static int nsvg__parseNumber(ReadOnlySpan<char> s, out ReadOnlySpan<char> literal)
	{
		int i = 0;
		// sign
		if (i < s.Length && (s[i] == '-' || s[i] == '+'))
			i++;
		// integer part
		while (i < s.Length && nsvg__isdigit(s[i]))
			i++;
		if (i < s.Length && s[i] == '.')
		{
			i++;
			while (i < s.Length && nsvg__isdigit(s[i]))
				i++;
		}
		// exponent
		if (i < s.Length && (s[i] == 'e' || s[i] == 'E') && (i + 1 >= s.Length || (s[i + 1] != 'm' && s[i + 1] != 'x')))
		{
			i++;
			if (i < s.Length && (s[i] == '-' || s[i] == '+'))
				i++;
			while (i < s.Length && nsvg__isdigit(s[i]))
				i++;
		}
		literal = s[..i];
		return i;
	}

	// Returns the number of chars consumed from 's' (including any leading whitespace/commas).
	static int nsvg__getNextPathItemWhenArcFlag(ReadOnlySpan<char> s, out ReadOnlySpan<char> item)
	{
		int start = 0;
		// Skip white spaces and commas
		while (start < s.Length && (nsvg__isspace(s[start]) || s[start] == ','))
			start++;
		var rest = s[start..];
		if (rest.Length == 0) { item = default; return start; }
		if (rest[0] == '0' || rest[0] == '1')
		{
			item = rest[..1];
			return start + 1;
		}
		item = default;
		return start;
	}

	// Returns the number of chars consumed from 's' (including any leading whitespace/commas).
	static int nsvg__getNextPathItem(ReadOnlySpan<char> s, out ReadOnlySpan<char> item)
	{
		int start = 0;
		// Skip white spaces and commas
		while (start < s.Length && (nsvg__isspace(s[start]) || s[start] == ','))
			start++;
		var rest = s[start..];
		if (rest.Length == 0) { item = default; return start; }
		if (rest[0] == '-' || rest[0] == '+' || rest[0] == '.' || nsvg__isdigit(rest[0]))
		{
			int n = nsvg__parseNumber(rest, out item);
			return start + n;
		}
		// Parse command
		item = rest[..1];
		return start + 1;
	}

	static NSVGunits nsvg__parseUnits(ReadOnlySpan<char> units)
	{
		if (units.Length >= 2)
		{
			if (units[0] == 'p' && units[1] == 'x')
				return NSVGunits.NSVG_UNITS_PX;
			if (units[0] == 'p' && units[1] == 't')
				return NSVGunits.NSVG_UNITS_PT;
			if (units[0] == 'p' && units[1] == 'c')
				return NSVGunits.NSVG_UNITS_PC;
			if (units[0] == 'm' && units[1] == 'm')
				return NSVGunits.NSVG_UNITS_MM;
			if (units[0] == 'c' && units[1] == 'm')
				return NSVGunits.NSVG_UNITS_CM;
			if (units[0] == 'i' && units[1] == 'n')
				return NSVGunits.NSVG_UNITS_IN;
			if (units[0] == 'e' && units[1] == 'm')
				return NSVGunits.NSVG_UNITS_EM;
			if (units[0] == 'e' && units[1] == 'x')
				return NSVGunits.NSVG_UNITS_EX;
		}
		if (units.Length >= 1 && units[0] == '%')
			return NSVGunits.NSVG_UNITS_PERCENT;
		return NSVGunits.NSVG_UNITS_USER;
	}

	static bool nsvg__isCoordinate(ReadOnlySpan<char> s)
	{
		// optional sign
		if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
			s = s[1..];
		// must have at least one digit, or start by a dot
		return s.Length > 0 && (nsvg__isdigit(s[0]) || s[0] == '.');
	}

	static NSVGcoordinate nsvg__parseCoordinateRaw(ReadOnlySpan<char> str)
	{
		var s = str;
		int n = nsvg__parseNumber(s, out var literal);
		var coord = new NSVGcoordinate
		{
			units = nsvg__parseUnits(s[n..]),
			value = (float)nsvg__atof(literal)
		};
		return coord;
	}

	static NSVGcoordinate nsvg__coord(float v, NSVGunits units)
	{
		var coord = new NSVGcoordinate { value = v, units = units };
		return coord;
	}

	static float nsvg__parseCoordinate(NSVGparser p, ReadOnlySpan<char> str, float orig, float length)
	{
		var coord = nsvg__parseCoordinateRaw(str);
		return nsvg__convertToPixels(p, coord, orig, length);
	}

	static bool nsvg__hexDigit(char c, out int v)
	{
		if (c >= '0' && c <= '9') { v = c - '0'; return true; }
		if (c >= 'a' && c <= 'f') { v = c - 'a' + 10; return true; }
		if (c >= 'A' && c <= 'F') { v = c - 'A' + 10; return true; }
		v = 0;
		return false;
	}

	static bool nsvg__hexPair(ReadOnlySpan<char> str, int pos, out byte v)
	{
		v = 0;
		if (pos + 1 >= str.Length) return false;
		if (!nsvg__hexDigit(str[pos], out var hi) || !nsvg__hexDigit(str[pos + 1], out var lo)) return false;
		v = (byte)(hi * 16 + lo);
		return true;
	}

	static uint nsvg__parseColorHex(ReadOnlySpan<char> str)
	{
		int pos = str.Length > 0 && str[0] == '#' ? 1 : 0;
		// 2 digit hex: #rrggbb
		if (str.Length - pos == 6 &&
			nsvg__hexPair(str, pos, out var r) &&
			nsvg__hexPair(str, pos + 2, out var g) &&
			nsvg__hexPair(str, pos + 4, out var b))
			return NSVG_RGB(r, g, b);
		// 1 digit hex, e.g. #abc -> 0xccbbaa
		if (str.Length - pos == 3 &&
			nsvg__hexDigit(str[pos], out var rd) &&
			nsvg__hexDigit(str[pos + 1], out var gd) &&
			nsvg__hexDigit(str[pos + 2], out var bd))
			return NSVG_RGB((byte)(rd * 17), (byte)(gd * 17), (byte)(bd * 17));
		return NSVG_RGB(128, 128, 128);
	}

	// Try to parse "rgb(255, 0, 128)" with integer components.
	static bool nsvg__tryParseRgbInt(ReadOnlySpan<char> str, Span<uint> rgbi)
	{
		var s = str;
		if (!s.StartsWith("rgb(")) return false;
		s = s[4..];
		for (int i = 0; i < 3; i++)
		{
			while (s.Length > 0 && nsvg__isspace(s[0])) s = s[1..];
			long sign = 1;
			if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
			{
				if (s[0] == '-') sign = -1;
				s = s[1..];
			}
			long v = 0;
			bool any = false;
			while (s.Length > 0 && nsvg__isdigit(s[0]))
			{
				v = v * 10 + (s[0] - '0');
				any = true;
				s = s[1..];
			}
			if (!any) return false;
			rgbi[i] = (uint)(sign * v);
			while (s.Length > 0 && nsvg__isspace(s[0])) s = s[1..];
			if (i < 2)
			{
				if (s.Length > 0 && s[0] == ',') s = s[1..];
				else return false;
			}
			else
			{
				if (s.Length > 0 && s[0] == ')') s = s[1..];
				else return false;
			}
		}
		return true;
	}

	// Parse rgb color. The pointer 'str' must point at "rgb(" (4+ characters).
	// Returns gray (rgb(128, 128, 128) == '#808080') on parse errors for backwards compatibility.
	static uint nsvg__parseColorRGB(ReadOnlySpan<char> str)
	{
		Span<uint> rgbi = stackalloc uint[3];
		Span<float> rgbf = stackalloc float[3];

		// try decimal integers first
		if (!nsvg__tryParseRgbInt(str, rgbi))
		{
			// integers failed, try percent values (float, locale independent)
			var s = str.Length >= 4 ? str[4..] : default; // skip "rgb("
			int i;
			for (i = 0; i < 3; i++)
			{
				while (s.Length > 0 && nsvg__isspace(s[0])) s = s[1..]; // skip leading spaces
				if (s.Length > 0 && s[0] == '+') s = s[1..]; // skip '+' (don't allow '-')
				if (s.Length == 0) break;
				int numLen = nsvg__parseNumber(s, out var lit);
				s = s[numLen..];
				rgbf[i] = (float)nsvg__atof(lit);
				if (s.Length > 0 && s[0] == '%') s = s[1..]; else break;
				while (s.Length > 0 && nsvg__isspace(s[0])) s = s[1..];
				if (s.Length > 0 && (i < 2 ? s[0] == ',' : s[0] == ')')) s = s[1..]; else break;
			}
			if (i == 3)
			{
				rgbi[0] = (uint)MathF.Round(rgbf[0] * 2.55f);
				rgbi[1] = (uint)MathF.Round(rgbf[1] * 2.55f);
				rgbi[2] = (uint)MathF.Round(rgbf[2] * 2.55f);
			}
			else
			{
				rgbi[0] = rgbi[1] = rgbi[2] = 128;
			}
		}

		// clip values as the CSS spec requires
		for (int i = 0; i < 3; i++)
			if (rgbi[i] > 255) rgbi[i] = 255;

		return NSVG_RGB((byte)rgbi[0], (byte)rgbi[1], (byte)rgbi[2]);
	}

	private static readonly (string Name, uint Color)[] nsvg__colors = new (string, uint)[]
	{
		("red", NSVG_RGB(255, 0, 0)),
		("green", NSVG_RGB( 0, 128, 0)),
		("blue", NSVG_RGB( 0, 0, 255)),
		("yellow", NSVG_RGB(255, 255, 0)),
		("cyan", NSVG_RGB( 0, 255, 255)),
		("magenta", NSVG_RGB(255, 0, 255)),
		("black", NSVG_RGB( 0, 0, 0)),
		("grey", NSVG_RGB(128, 128, 128)),
		("gray", NSVG_RGB(128, 128, 128)),
		("white", NSVG_RGB(255, 255, 255)),

		("aliceblue", NSVG_RGB(240, 248, 255)),
		("antiquewhite", NSVG_RGB(250, 235, 215)),
		("aqua", NSVG_RGB( 0, 255, 255)),
		("aquamarine", NSVG_RGB(127, 255, 212)),
		("azure", NSVG_RGB(240, 255, 255)),
		("beige", NSVG_RGB(245, 245, 220)),
		("bisque", NSVG_RGB(255, 228, 196)),
		("blanchedalmond", NSVG_RGB(255, 235, 205)),
		("blueviolet", NSVG_RGB(138, 43, 226)),
		("brown", NSVG_RGB(165, 42, 42)),
		("burlywood", NSVG_RGB(222, 184, 135)),
		("cadetblue", NSVG_RGB( 95, 158, 160)),
		("chartreuse", NSVG_RGB(127, 255, 0)),
		("chocolate", NSVG_RGB(210, 105, 30)),
		("coral", NSVG_RGB(255, 127, 80)),
		("cornflowerblue", NSVG_RGB(100, 149, 237)),
		("cornsilk", NSVG_RGB(255, 248, 220)),
		("crimson", NSVG_RGB(220, 20, 60)),
		("darkblue", NSVG_RGB( 0, 0, 139)),
		("darkcyan", NSVG_RGB( 0, 139, 139)),
		("darkgoldenrod", NSVG_RGB(184, 134, 11)),
		("darkgray", NSVG_RGB(169, 169, 169)),
		("darkgreen", NSVG_RGB( 0, 100, 0)),
		("darkgrey", NSVG_RGB(169, 169, 169)),
		("darkkhaki", NSVG_RGB(189, 183, 107)),
		("darkmagenta", NSVG_RGB(139, 0, 139)),
		("darkolivegreen", NSVG_RGB( 85, 107, 47)),
		("darkorange", NSVG_RGB(255, 140, 0)),
		("darkorchid", NSVG_RGB(153, 50, 204)),
		("darkred", NSVG_RGB(139, 0, 0)),
		("darksalmon", NSVG_RGB(233, 150, 122)),
		("darkseagreen", NSVG_RGB(143, 188, 143)),
		("darkslateblue", NSVG_RGB( 72, 61, 139)),
		("darkslategray", NSVG_RGB( 47, 79, 79)),
		("darkslategrey", NSVG_RGB( 47, 79, 79)),
		("darkturquoise", NSVG_RGB( 0, 206, 209)),
		("darkviolet", NSVG_RGB(148, 0, 211)),
		("deeppink", NSVG_RGB(255, 20, 147)),
		("deepskyblue", NSVG_RGB( 0, 191, 255)),
		("dimgray", NSVG_RGB(105, 105, 105)),
		("dimgrey", NSVG_RGB(105, 105, 105)),
		("dodgerblue", NSVG_RGB( 30, 144, 255)),
		("firebrick", NSVG_RGB(178, 34, 34)),
		("floralwhite", NSVG_RGB(255, 250, 240)),
		("forestgreen", NSVG_RGB( 34, 139, 34)),
		("fuchsia", NSVG_RGB(255, 0, 255)),
		("gainsboro", NSVG_RGB(220, 220, 220)),
		("ghostwhite", NSVG_RGB(248, 248, 255)),
		("gold", NSVG_RGB(255, 215, 0)),
		("goldenrod", NSVG_RGB(218, 165, 32)),
		("greenyellow", NSVG_RGB(173, 255, 47)),
		("honeydew", NSVG_RGB(240, 255, 240)),
		("hotpink", NSVG_RGB(255, 105, 180)),
		("indianred", NSVG_RGB(205, 92, 92)),
		("indigo", NSVG_RGB( 75, 0, 130)),
		("ivory", NSVG_RGB(255, 255, 240)),
		("khaki", NSVG_RGB(240, 230, 140)),
		("lavender", NSVG_RGB(230, 230, 250)),
		("lavenderblush", NSVG_RGB(255, 240, 245)),
		("lawngreen", NSVG_RGB(124, 252, 0)),
		("lemonchiffon", NSVG_RGB(255, 250, 205)),
		("lightblue", NSVG_RGB(173, 216, 230)),
		("lightcoral", NSVG_RGB(240, 128, 128)),
		("lightcyan", NSVG_RGB(224, 255, 255)),
		("lightgoldenrodyellow", NSVG_RGB(250, 250, 210)),
		("lightgray", NSVG_RGB(211, 211, 211)),
		("lightgreen", NSVG_RGB(144, 238, 144)),
		("lightgrey", NSVG_RGB(211, 211, 211)),
		("lightpink", NSVG_RGB(255, 182, 193)),
		("lightsalmon", NSVG_RGB(255, 160, 122)),
		("lightseagreen", NSVG_RGB( 32, 178, 170)),
		("lightskyblue", NSVG_RGB(135, 206, 250)),
		("lightslategray", NSVG_RGB(119, 136, 153)),
		("lightslategrey", NSVG_RGB(119, 136, 153)),
		("lightsteelblue", NSVG_RGB(176, 196, 222)),
		("lightyellow", NSVG_RGB(255, 255, 224)),
		("lime", NSVG_RGB( 0, 255, 0)),
		("limegreen", NSVG_RGB( 50, 205, 50)),
		("linen", NSVG_RGB(250, 240, 230)),
		("maroon", NSVG_RGB(128, 0, 0)),
		("mediumaquamarine", NSVG_RGB(102, 205, 170)),
		("mediumblue", NSVG_RGB( 0, 0, 205)),
		("mediumorchid", NSVG_RGB(186, 85, 211)),
		("mediumpurple", NSVG_RGB(147, 112, 219)),
		("mediumseagreen", NSVG_RGB( 60, 179, 113)),
		("mediumslateblue", NSVG_RGB(123, 104, 238)),
		("mediumspringgreen", NSVG_RGB( 0, 250, 154)),
		("mediumturquoise", NSVG_RGB( 72, 209, 204)),
		("mediumvioletred", NSVG_RGB(199, 21, 133)),
		("midnightblue", NSVG_RGB( 25, 25, 112)),
		("mintcream", NSVG_RGB(245, 255, 250)),
		("mistyrose", NSVG_RGB(255, 228, 225)),
		("moccasin", NSVG_RGB(255, 228, 181)),
		("navajowhite", NSVG_RGB(255, 222, 173)),
		("navy", NSVG_RGB( 0, 0, 128)),
		("oldlace", NSVG_RGB(253, 245, 230)),
		("olive", NSVG_RGB(128, 128, 0)),
		("olivedrab", NSVG_RGB(107, 142, 35)),
		("orange", NSVG_RGB(255, 165, 0)),
		("orangered", NSVG_RGB(255, 69, 0)),
		("orchid", NSVG_RGB(218, 112, 214)),
		("palegoldenrod", NSVG_RGB(238, 232, 170)),
		("palegreen", NSVG_RGB(152, 251, 152)),
		("paleturquoise", NSVG_RGB(175, 238, 238)),
		("palevioletred", NSVG_RGB(219, 112, 147)),
		("papayawhip", NSVG_RGB(255, 239, 213)),
		("peachpuff", NSVG_RGB(255, 218, 185)),
		("peru", NSVG_RGB(205, 133, 63)),
		("pink", NSVG_RGB(255, 192, 203)),
		("plum", NSVG_RGB(221, 160, 221)),
		("powderblue", NSVG_RGB(176, 224, 230)),
		("purple", NSVG_RGB(128, 0, 128)),
		("rebeccapurple", NSVG_RGB(102, 51, 153)),
		("rosybrown", NSVG_RGB(188, 143, 143)),
		("royalblue", NSVG_RGB( 65, 105, 225)),
		("saddlebrown", NSVG_RGB(139, 69, 19)),
		("salmon", NSVG_RGB(250, 128, 114)),
		("sandybrown", NSVG_RGB(244, 164, 96)),
		("seagreen", NSVG_RGB( 46, 139, 87)),
		("seashell", NSVG_RGB(255, 245, 238)),
		("sienna", NSVG_RGB(160, 82, 45)),
		("silver", NSVG_RGB(192, 192, 192)),
		("skyblue", NSVG_RGB(135, 206, 235)),
		("slateblue", NSVG_RGB(106, 90, 205)),
		("slategray", NSVG_RGB(112, 128, 144)),
		("slategrey", NSVG_RGB(112, 128, 144)),
		("snow", NSVG_RGB(255, 250, 250)),
		("springgreen", NSVG_RGB( 0, 255, 127)),
		("steelblue", NSVG_RGB( 70, 130, 180)),
		("tan", NSVG_RGB(210, 180, 140)),
		("teal", NSVG_RGB( 0, 128, 128)),
		("thistle", NSVG_RGB(216, 191, 216)),
		("tomato", NSVG_RGB(255, 99, 71)),
		("turquoise", NSVG_RGB( 64, 224, 208)),
		("violet", NSVG_RGB(238, 130, 238)),
		("wheat", NSVG_RGB(245, 222, 179)),
		("whitesmoke", NSVG_RGB(245, 245, 245)),
		("yellowgreen", NSVG_RGB(154, 205, 50)),
	};

	static uint nsvg__parseColorName(ReadOnlySpan<char> str)
	{
		foreach (var (name, color) in nsvg__colors)
		{
			if (str.SequenceEqual(name))
				return color;
		}
		return NSVG_RGB(128, 128, 128);
	}

	static uint nsvg__parseColor(ReadOnlySpan<char> str)
	{
		while (str.Length > 0 && str[0] == ' ') str = str[1..];
		if (str.Length >= 1 && str[0] == '#')
			return nsvg__parseColorHex(str);
		if (str.Length >= 4 && str[0] == 'r' && str[1] == 'g' && str[2] == 'b' && str[3] == '(')
			return nsvg__parseColorRGB(str);
		return nsvg__parseColorName(str);
	}

	static float nsvg__parseOpacity(ReadOnlySpan<char> str)
	{
		float val = (float)nsvg__atof(str);
		if (val < 0.0f) val = 0.0f;
		if (val > 1.0f) val = 1.0f;
		return val;
	}

	static float nsvg__parseMiterLimit(ReadOnlySpan<char> str)
	{
		float val = (float)nsvg__atof(str);
		if (val < 0.0f) val = 0.0f;
		return val;
	}

	static int nsvg__parseTransformArgs(ReadOnlySpan<char> str, Span<float> args, int maxNa, out int na)
	{
		na = 0;
		int p = 0;
		while (p < str.Length && str[p] != '(') p++;
		if (p >= str.Length) return 1;
		int e = p;
		while (e < str.Length && str[e] != ')') e++;
		if (e >= str.Length) return 1;

		int cur = p + 1;
		while (cur < e)
		{
			if (str[cur] == '-' || str[cur] == '+' || str[cur] == '.' || nsvg__isdigit(str[cur]))
			{
				if (na >= maxNa) return 0;
				int numLen = nsvg__parseNumber(str[cur..e], out var lit);
				args[na++] = (float)nsvg__atof(lit);
				cur += numLen;
			}
			else
			{
				cur++;
			}
		}
		return e;
	}

	static int nsvg__parseMatrix(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, t, 6, out int na);
		if (na != 6) return len;
		xform = t;
		return len;
	}

	static int nsvg__parseTranslate(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		Span<float> args = stackalloc float[2];
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, args, 2, out int na);
		if (na == 1) args[1] = 0.0f;
		nsvg__xformSetTranslation(ref t, args[0], args[1]);
		xform = t;
		return len;
	}

	static int nsvg__parseScale(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		Span<float> args = stackalloc float[2];
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, args, 2, out int na);
		if (na == 1) args[1] = args[0];
		nsvg__xformSetScale(ref t, args[0], args[1]);
		xform = t;
		return len;
	}

	static int nsvg__parseSkewX(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		Span<float> args = stackalloc float[1];
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, args, 1, out int na);
		nsvg__xformSetSkewX(ref t, args[0] / 180.0f * NSVG_PI);
		xform = t;
		return len;
	}

	static int nsvg__parseSkewY(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		Span<float> args = stackalloc float[1];
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, args, 1, out int na);
		nsvg__xformSetSkewY(ref t, args[0] / 180.0f * NSVG_PI);
		xform = t;
		return len;
	}

	static int nsvg__parseRotate(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		Span<float> args = stackalloc float[3];
		InlineArray6<float> m = default;
		InlineArray6<float> t = default;
		int len = nsvg__parseTransformArgs(str, args, 3, out int na);
		if (na == 1)
			args[1] = args[2] = 0.0f;
		nsvg__xformIdentity(ref m);

		if (na > 1)
		{
			nsvg__xformSetTranslation(ref t, -args[1], -args[2]);
			nsvg__xformMultiply(ref m, in t);
		}

		nsvg__xformSetRotation(ref t, args[0] / 180.0f * NSVG_PI);
		nsvg__xformMultiply(ref m, in t);

		if (na > 1)
		{
			nsvg__xformSetTranslation(ref t, args[1], args[2]);
			nsvg__xformMultiply(ref m, in t);
		}

		xform = m;
		return len;
	}

	static void nsvg__parseTransform(ref InlineArray6<float> xform, ReadOnlySpan<char> str)
	{
		InlineArray6<float> t = default;
		nsvg__xformIdentity(ref xform);
		while (str.Length > 0)
		{
			int len;
			if (str.StartsWith("matrix"))
				len = nsvg__parseMatrix(ref t, str);
			else if (str.StartsWith("translate"))
				len = nsvg__parseTranslate(ref t, str);
			else if (str.StartsWith("scale"))
				len = nsvg__parseScale(ref t, str);
			else if (str.StartsWith("rotate"))
				len = nsvg__parseRotate(ref t, str);
			else if (str.StartsWith("skewX"))
				len = nsvg__parseSkewX(ref t, str);
			else if (str.StartsWith("skewY"))
				len = nsvg__parseSkewY(ref t, str);
			else
			{
				str = str[1..];
				continue;
			}
			if (len != 0)
			{
				str = str[len..];
			}
			else
			{
				str = str[1..];
				continue;
			}

			nsvg__xformPremultiply(ref xform, ref t);
		}
	}

	static string nsvg__parseUrl(ReadOnlySpan<char> str)
	{
		var s = str;
		s = s[4..]; // "url(";
		if (s.Length > 0 && s[0] == '#') s = s[1..];
		int i = 0;
		while (i < 63 && i < s.Length && s[i] != ')') i++;
		return new string(s[..i]);
	}

	static NSVGlineCap nsvg__parseLineCap(ReadOnlySpan<char> str)
	{
		if (str is "butt") return NSVGlineCap.NSVG_CAP_BUTT;
		if (str is "round") return NSVGlineCap.NSVG_CAP_ROUND;
		if (str is "square") return NSVGlineCap.NSVG_CAP_SQUARE;
		return NSVGlineCap.NSVG_CAP_BUTT;
	}

	static NSVGlineJoin nsvg__parseLineJoin(ReadOnlySpan<char> str)
	{
		if (str is "miter") return NSVGlineJoin.NSVG_JOIN_MITER;
		if (str is "round") return NSVGlineJoin.NSVG_JOIN_ROUND;
		if (str is "bevel") return NSVGlineJoin.NSVG_JOIN_BEVEL;
		return NSVGlineJoin.NSVG_JOIN_MITER;
	}

	static NSVGfillRule nsvg__parseFillRule(ReadOnlySpan<char> str)
	{
		if (str is "nonzero") return NSVGfillRule.NSVG_FILLRULE_NONZERO;
		if (str is "evenodd") return NSVGfillRule.NSVG_FILLRULE_EVENODD;
		return NSVGfillRule.NSVG_FILLRULE_NONZERO;
	}

	static byte nsvg__parsePaintOrder(ReadOnlySpan<char> str)
	{
		if (str is "normal" || str is "fill stroke markers")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_MARKERS);
		if (str is "fill markers stroke")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_MARKERS, NSVGpaintOrder.NSVG_PAINT_STROKE);
		if (str is "markers fill stroke")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_MARKERS, NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_STROKE);
		if (str is "markers stroke fill")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_MARKERS, NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_FILL);
		if (str is "stroke fill markers")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_MARKERS);
		if (str is "stroke markers fill")
			return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_MARKERS, NSVGpaintOrder.NSVG_PAINT_FILL);
		return nsvg__encodePaintOrder(NSVGpaintOrder.NSVG_PAINT_FILL, NSVGpaintOrder.NSVG_PAINT_STROKE, NSVGpaintOrder.NSVG_PAINT_MARKERS);
	}

	static int nsvg__getNextDashItem(ReadOnlySpan<char> s, out ReadOnlySpan<char> item)
	{
		int start = 0;
		// Skip white spaces and commas
		while (start < s.Length && (nsvg__isspace(s[start]) || s[start] == ','))
			start++;
		int n = start;
		while (n < s.Length && (!nsvg__isspace(s[n]) && s[n] != ',')) n++;
		item = s[start..n];
		return n;
	}

	static int nsvg__parseStrokeDashArray(NSVGparser p, ReadOnlySpan<char> str, ref InlineArray8<float> strokeDashArray)
	{
		int count = 0;
		float sum = 0.0f;

		// Handle "none"
		if (str.Length > 0 && str[0] == 'n')
			return 0;

		var s = str;
		// Parse dashes
		while (s.Length > 0)
		{
			int n = nsvg__getNextDashItem(s, out var item);
			s = s[n..];
			if (item.Length == 0) break;
			if (count < NSVG_MAX_DASHES)
				strokeDashArray[count++] = MathF.Abs(nsvg__parseCoordinate(p, item, 0.0f, nsvg__actualLength(p)));
		}

		for (int i = 0; i < count; i++)
			sum += strokeDashArray[i];
		if (sum <= 1e-6f)
			count = 0;

		return count;
	}

	// Apply any matching class styles for a "class" attribute value. We support only simple class
	// selectors. The class attribute may contain multiple space-separated class names.
	static void nsvg__applyClassStyles(NSVGparser p, ReadOnlySpan<char> value)
	{
		var cur = value;
		while (cur.Length > 0)
		{
			while (cur.Length > 0 && nsvg__isspace(cur[0])) cur = cur[1..];
			if (cur.Length == 0) break;

			int classLen = 0;
			while (classLen < cur.Length && !nsvg__isspace(cur[classLen])) classLen++;
			var className = cur[..classLen];
			cur = cur[classLen..];

			foreach (var style in p.styles)
			{
				if (style.className.AsSpan().SequenceEqual(className))
				{
					nsvg__parseStyle(p, style.propertiesText.AsSpan());
				}
			}
		}
	}

	static bool nsvg__parseAttr(NSVGparser p, ReadOnlySpan<char> name, ReadOnlySpan<char> value)
	{
		ref NSVGattrib attr = ref nsvg__getAttr(p);

		if (name is "style")
		{
			nsvg__parseStyle(p, value);
		}
		else if (name is "display")
		{
			if (value is "none")
				attr.visible = 0;
			// Don't reset ->visible on display:inline, one display:none hides the whole subtree
		}
		else if (name is "fill")
		{
			if (value is "none")
				attr.hasFill = 0;
			else if (value.StartsWith("url("))
			{
				attr.hasFill = 2;
				attr.fillGradient = nsvg__parseUrl(value);
			}
			else
			{
				attr.hasFill = 1;
				attr.fillColor = nsvg__parseColor(value);
			}
		}
		else if (name is "opacity")
		{
			attr.opacity = nsvg__parseOpacity(value);
		}
		else if (name is "fill-opacity")
		{
			attr.fillOpacity = nsvg__parseOpacity(value);
		}
		else if (name is "stroke")
		{
			if (value is "none")
				attr.hasStroke = 0;
			else if (value.StartsWith("url("))
			{
				attr.hasStroke = 2;
				attr.strokeGradient = nsvg__parseUrl(value);
			}
			else
			{
				attr.hasStroke = 1;
				attr.strokeColor = nsvg__parseColor(value);
			}
		}
		else if (name is "stroke-width")
		{
			attr.strokeWidth = nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualLength(p));
		}
		else if (name is "stroke-dasharray")
		{
			attr.strokeDashCount = nsvg__parseStrokeDashArray(p, value, ref attr.strokeDashArray);
		}
		else if (name is "stroke-dashoffset")
		{
			attr.strokeDashOffset = nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualLength(p));
		}
		else if (name is "stroke-opacity")
		{
			attr.strokeOpacity = nsvg__parseOpacity(value);
		}
		else if (name is "stroke-linecap")
		{
			attr.strokeLineCap = nsvg__parseLineCap(value);
		}
		else if (name is "stroke-linejoin")
		{
			attr.strokeLineJoin = nsvg__parseLineJoin(value);
		}
		else if (name is "stroke-miterlimit")
		{
			attr.miterLimit = nsvg__parseMiterLimit(value);
		}
		else if (name is "fill-rule")
		{
			attr.fillRule = nsvg__parseFillRule(value);
		}
		else if (name is "font-size")
		{
			attr.fontSize = nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualLength(p));
		}
		else if (name is "transform")
		{
			InlineArray6<float> xform = default;
			nsvg__parseTransform(ref xform, value);
			nsvg__xformPremultiply(ref attr.xform, ref xform);
		}
		else if (name is "stop-color")
		{
			attr.stopColor = nsvg__parseColor(value);
		}
		else if (name is "stop-opacity")
		{
			attr.stopOpacity = nsvg__parseOpacity(value);
		}
		else if (name is "offset")
		{
			attr.stopOffset = nsvg__parseCoordinate(p, value, 0.0f, 1.0f);
		}
		else if (name is "paint-order")
		{
			attr.paintOrder = nsvg__parsePaintOrder(value);
		}
		else if (name is "id")
		{
			attr.id = new string(value);
		}
		else if (name is "class")
		{
			nsvg__applyClassStyles(p, value);
		}
		else
		{
			return false;
		}
		return true;
	}

	static bool nsvg__parseNameValue(NSVGparser p, ReadOnlySpan<char> span)
	{
		int colon = 0;
		while (colon < span.Length && span[colon] != ':') colon++;

		// If there is no ':' the whole span is the (malformed) name with no value.
		if (colon == span.Length)
		{
			return nsvg__parseAttr(p, span, default);
		}

		// Right trim the name (drop the ':' and any spaces before it).
		int nameEnd = colon;
		while (nameEnd > 0 && nsvg__isspace(span[nameEnd - 1]))
			nameEnd--;
		var name = span[..nameEnd];

		// Left trim the value (skip the ':' and any spaces after it).
		int val = colon + 1;
		while (val < span.Length && nsvg__isspace(span[val]))
			val++;
		var value = span[val..];

		return nsvg__parseAttr(p, name, value);
	}

	static void nsvg__parseStyle(NSVGparser p, ReadOnlySpan<char> str)
	{
		while (str.Length > 0)
		{
			// Left Trim
			while (str.Length > 0 && nsvg__isspace(str[0])) str = str[1..];
			var start = str;
			while (str.Length > 0 && str[0] != ';') str = str[1..];

			var content = start[..(start.Length - str.Length)];

			// Right Trim trailing whitespace (the ';' is already excluded).
			while (content.Length > 0 && nsvg__isspace(content[^1]))
				content = content[..^1];

			nsvg__parseNameValue(p, content);

			if (str.Length > 0) str = str[1..];
		}
	}

	static void nsvg__parseAttribs(NSVGparser p, ReadOnlySpan<string> attr)
	{
		for (int i = 0; i < attr.Length; i += 2)
		{
			var name = attr[i].AsSpan();
			var value = attr[i + 1].AsSpan();
			if (name is "style")
				nsvg__parseStyle(p, value);
			else
				nsvg__parseAttr(p, name, value);
		}
	}

	static int nsvg__getArgsPerElement(char cmd)
	{
		switch (cmd)
		{
			case 'v':
			case 'V':
			case 'h':
			case 'H':
				return 1;
			case 'm':
			case 'M':
			case 'l':
			case 'L':
			case 't':
			case 'T':
				return 2;
			case 'q':
			case 'Q':
			case 's':
			case 'S':
				return 4;
			case 'c':
			case 'C':
				return 6;
			case 'a':
			case 'A':
				return 7;
			case 'z':
			case 'Z':
				return 0;
		}
		return -1;
	}

	static void nsvg__pathMoveTo(NSVGparser p, ref float cpx, ref float cpy, ReadOnlySpan<float> args, bool rel)
	{
		if (rel)
		{
			cpx += args[0];
			cpy += args[1];
		}
		else
		{
			cpx = args[0];
			cpy = args[1];
		}
		nsvg__moveTo(p, cpx, cpy);
	}

	static void nsvg__pathLineTo(NSVGparser p, ref float cpx, ref float cpy, ReadOnlySpan<float> args, bool rel)
	{
		if (rel)
		{
			cpx += args[0];
			cpy += args[1];
		}
		else
		{
			cpx = args[0];
			cpy = args[1];
		}
		nsvg__lineTo(p, cpx, cpy);
	}

	static void nsvg__pathHLineTo(NSVGparser p, ref float cpx, ref float cpy, ReadOnlySpan<float> args, bool rel)
	{
		if (rel)
			cpx += args[0];
		else
			cpx = args[0];
		nsvg__lineTo(p, cpx, cpy);
	}

	static void nsvg__pathVLineTo(NSVGparser p, ref float cpx, ref float cpy, ReadOnlySpan<float> args, bool rel)
	{
		if (rel)
			cpy += args[0];
		else
			cpy = args[0];
		nsvg__lineTo(p, cpx, cpy);
	}

	static void nsvg__pathCubicBezTo(NSVGparser p, ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, ReadOnlySpan<float> args, bool rel)
	{
		float x2, y2, cx1, cy1, cx2, cy2;

		if (rel)
		{
			cx1 = cpx + args[0];
			cy1 = cpy + args[1];
			cx2 = cpx + args[2];
			cy2 = cpy + args[3];
			x2 = cpx + args[4];
			y2 = cpy + args[5];
		}
		else
		{
			cx1 = args[0];
			cy1 = args[1];
			cx2 = args[2];
			cy2 = args[3];
			x2 = args[4];
			y2 = args[5];
		}

		nsvg__cubicBezTo(p, cx1, cy1, cx2, cy2, x2, y2);

		cpx2 = cx2;
		cpy2 = cy2;
		cpx = x2;
		cpy = y2;
	}

	static void nsvg__pathCubicBezShortTo(NSVGparser p, ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, ReadOnlySpan<float> args, bool rel)
	{
		float x1, y1, x2, y2, cx1, cy1, cx2, cy2;

		x1 = cpx;
		y1 = cpy;
		if (rel)
		{
			cx2 = cpx + args[0];
			cy2 = cpy + args[1];
			x2 = cpx + args[2];
			y2 = cpy + args[3];
		}
		else
		{
			cx2 = args[0];
			cy2 = args[1];
			x2 = args[2];
			y2 = args[3];
		}

		cx1 = 2 * x1 - cpx2;
		cy1 = 2 * y1 - cpy2;

		nsvg__cubicBezTo(p, cx1, cy1, cx2, cy2, x2, y2);

		cpx2 = cx2;
		cpy2 = cy2;
		cpx = x2;
		cpy = y2;
	}

	static void nsvg__pathQuadBezTo(NSVGparser p, ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, ReadOnlySpan<float> args, bool rel)
	{
		float x1, y1, x2, y2, cx, cy;
		float cx1, cy1, cx2, cy2;

		x1 = cpx;
		y1 = cpy;
		if (rel)
		{
			cx = cpx + args[0];
			cy = cpy + args[1];
			x2 = cpx + args[2];
			y2 = cpy + args[3];
		}
		else
		{
			cx = args[0];
			cy = args[1];
			x2 = args[2];
			y2 = args[3];
		}

		// Convert to cubic bezier
		cx1 = x1 + 2.0f / 3.0f * (cx - x1);
		cy1 = y1 + 2.0f / 3.0f * (cy - y1);
		cx2 = x2 + 2.0f / 3.0f * (cx - x2);
		cy2 = y2 + 2.0f / 3.0f * (cy - y2);

		nsvg__cubicBezTo(p, cx1, cy1, cx2, cy2, x2, y2);

		cpx2 = cx;
		cpy2 = cy;
		cpx = x2;
		cpy = y2;
	}

	static void nsvg__pathQuadBezShortTo(NSVGparser p, ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, ReadOnlySpan<float> args, bool rel)
	{
		float x1, y1, x2, y2, cx, cy;
		float cx1, cy1, cx2, cy2;

		x1 = cpx;
		y1 = cpy;
		if (rel)
		{
			x2 = cpx + args[0];
			y2 = cpy + args[1];
		}
		else
		{
			x2 = args[0];
			y2 = args[1];
		}

		cx = 2 * x1 - cpx2;
		cy = 2 * y1 - cpy2;

		// Convert to cubic bezier
		cx1 = x1 + 2.0f / 3.0f * (cx - x1);
		cy1 = y1 + 2.0f / 3.0f * (cy - y1);
		cx2 = x2 + 2.0f / 3.0f * (cx - x2);
		cy2 = y2 + 2.0f / 3.0f * (cy - y2);

		nsvg__cubicBezTo(p, cx1, cy1, cx2, cy2, x2, y2);

		cpx2 = cx;
		cpy2 = cy;
		cpx = x2;
		cpy = y2;
	}

	static float nsvg__sqr(float x) { return x * x; }

	static float nsvg__vmag(float x, float y) { return MathF.Sqrt(x * x + y * y); }

	static float nsvg__vecrat(float ux, float uy, float vx, float vy)
	{
		return (ux * vx + uy * vy) / (nsvg__vmag(ux, uy) * nsvg__vmag(vx, vy));
	}

	static float nsvg__vecang(float ux, float uy, float vx, float vy)
	{
		float r = nsvg__vecrat(ux, uy, vx, vy);
		if (r < -1.0f) r = -1.0f;
		if (r > 1.0f) r = 1.0f;
		return ((ux * vy < uy * vx) ? -1.0f : 1.0f) * MathF.Acos(r);
	}

	static void nsvg__pathArcTo(NSVGparser p, ref float cpx, ref float cpy, ReadOnlySpan<float> args, bool rel)
	{
		// Ported from canvg (https://code.google.com/p/canvg/)
		float rx, ry, rotx;
		float x1, y1, x2, y2, cx, cy, dx, dy, d;
		float x1p, y1p, cxp, cyp, s, sa, sb;
		float ux, uy, vx, vy, a1, da;
		float x = 0, y = 0, tanx = 0, tany = 0, a, px = 0, py = 0, ptanx = 0, ptany = 0;
		InlineArray6<float> t = default;
		float sinrx, cosrx;
		int fa, fs;
		int i, ndivs;
		float hda, kappa;

		rx = MathF.Abs(args[0]);				// y radius
		ry = MathF.Abs(args[1]);				// x radius
		rotx = args[2] / 180.0f * NSVG_PI;		// x rotation angle
		fa = MathF.Abs(args[3]) > 1e-6f ? 1 : 0;	// Large arc
		fs = MathF.Abs(args[4]) > 1e-6f ? 1 : 0;	// Sweep direction
		x1 = cpx;							// start point
		y1 = cpy;
		if (rel)							// end point
		{
			x2 = cpx + args[5];
			y2 = cpy + args[6];
		}
		else
		{
			x2 = args[5];
			y2 = args[6];
		}

		dx = x1 - x2;
		dy = y1 - y2;
		d = MathF.Sqrt(dx * dx + dy * dy);
		if (d < 1e-6f || rx < 1e-6f || ry < 1e-6f)
		{
			// The arc degenerates to a line
			nsvg__lineTo(p, x2, y2);
			cpx = x2;
			cpy = y2;
			return;
		}

		sinrx = MathF.Sin(rotx);
		cosrx = MathF.Cos(rotx);

		// Convert to center point parameterization.
		// http://www.w3.org/TR/SVG11/implnote.html#ArcImplementationNotes
		// 1) Compute x1', y1'
		x1p = cosrx * dx / 2.0f + sinrx * dy / 2.0f;
		y1p = -sinrx * dx / 2.0f + cosrx * dy / 2.0f;
		d = nsvg__sqr(x1p) / nsvg__sqr(rx) + nsvg__sqr(y1p) / nsvg__sqr(ry);
		if (d > 1)
		{
			d = MathF.Sqrt(d);
			rx *= d;
			ry *= d;
		}
		// 2) Compute cx', cy'
		s = 0.0f;
		sa = nsvg__sqr(rx) * nsvg__sqr(ry) - nsvg__sqr(rx) * nsvg__sqr(y1p) - nsvg__sqr(ry) * nsvg__sqr(x1p);
		sb = nsvg__sqr(rx) * nsvg__sqr(y1p) + nsvg__sqr(ry) * nsvg__sqr(x1p);
		if (sa < 0.0f) sa = 0.0f;
		if (sb > 0.0f)
			s = MathF.Sqrt(sa / sb);
		if (fa == fs)
			s = -s;
		cxp = s * rx * y1p / ry;
		cyp = s * -ry * x1p / rx;

		// 3) Compute cx,cy from cx',cy'
		cx = (x1 + x2) / 2.0f + cosrx * cxp - sinrx * cyp;
		cy = (y1 + y2) / 2.0f + sinrx * cxp + cosrx * cyp;

		// 4) Calculate theta1, and delta theta.
		ux = (x1p - cxp) / rx;
		uy = (y1p - cyp) / ry;
		vx = (-x1p - cxp) / rx;
		vy = (-y1p - cyp) / ry;
		a1 = nsvg__vecang(1.0f, 0.0f, ux, uy);	// Initial angle
		da = nsvg__vecang(ux, uy, vx, vy);		// Delta angle

		if (fs == 0 && da > 0)
			da -= 2 * NSVG_PI;
		else if (fs == 1 && da < 0)
			da += 2 * NSVG_PI;

		// Approximate the arc using cubic spline segments.
		t[0] = cosrx; t[1] = sinrx;
		t[2] = -sinrx; t[3] = cosrx;
		t[4] = cx; t[5] = cy;

		// Split arc into max 90 degree segments.
		// The loop assumes an iteration per end point (including start and end), this +1.
		ndivs = (int)(MathF.Abs(da) / (NSVG_PI * 0.5f) + 1.0f);
		hda = (da / (float)ndivs) / 2.0f;
		// Fix for ticket #179: division by 0: avoid cotangens around 0 (infinite)
		if ((hda < 1e-3f) && (hda > -1e-3f))
			hda *= 0.5f;
		else
			hda = (1.0f - MathF.Cos(hda)) / MathF.Sin(hda);
		kappa = MathF.Abs(4.0f / 3.0f * hda);
		if (da < 0.0f)
			kappa = -kappa;

		for (i = 0; i <= ndivs; i++)
		{
			a = a1 + da * ((float)i / (float)ndivs);
			dx = MathF.Cos(a);
			dy = MathF.Sin(a);
			nsvg__xformPoint(ref x, ref y, dx * rx, dy * ry, t); // position
			nsvg__xformVec(ref tanx, ref tany, -dy * rx * kappa, dx * ry * kappa, t); // tangent
			if (i > 0)
				nsvg__cubicBezTo(p, px + ptanx, py + ptany, x - tanx, y - tany, x, y);
			px = x;
			py = y;
			ptanx = tanx;
			ptany = tany;
		}

		cpx = x2;
		cpy = y2;
	}

	static void nsvg__parsePath(NSVGparser p, ReadOnlySpan<string> attr)
	{
		ReadOnlySpan<char> s = default;
		char cmd = '\0';
		Span<float> args = stackalloc float[10];
		int nargs = 0;
		int rargs = 0;
		bool initPoint = false;
		float cpx = 0, cpy = 0, cpx2 = 0, cpy2 = 0;
		bool closedFlag = false;

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (attr[i] == "d")
			{
				s = attr[i + 1];
			}
			else
			{
				nsvg__parseAttribs(p, new[] { attr[i], attr[i + 1] });
			}
		}

		if (s.Length > 0)
		{
			nsvg__resetPath(p);
			cpx = 0; cpy = 0;
			cpx2 = 0; cpy2 = 0;
			initPoint = false;
			closedFlag = false;
			nargs = 0;

			var cur = s;
			while (cur.Length > 0)
			{
				var item = default(ReadOnlySpan<char>);
				if ((cmd == 'A' || cmd == 'a') && (nargs == 3 || nargs == 4))
					cur = cur[nsvg__getNextPathItemWhenArcFlag(cur, out item)..];
				if (item.Length == 0)
					cur = cur[nsvg__getNextPathItem(cur, out item)..];
				if (item.Length == 0) break;
				if (cmd != '\0' && nsvg__isCoordinate(item))
				{
					if (nargs < 10)
						args[nargs++] = (float)nsvg__atof(item);
					if (nargs >= rargs)
					{
						switch (cmd)
						{
							case 'm':
							case 'M':
								nsvg__pathMoveTo(p, ref cpx, ref cpy, args, cmd == 'm');
								// Moveto can be followed by multiple coordinate pairs,
								// which should be treated as linetos.
								cmd = (cmd == 'm') ? 'l' : 'L';
								rargs = nsvg__getArgsPerElement(cmd);
								cpx2 = cpx; cpy2 = cpy;
								initPoint = true;
								break;
							case 'l':
							case 'L':
								nsvg__pathLineTo(p, ref cpx, ref cpy, args, cmd == 'l');
								cpx2 = cpx; cpy2 = cpy;
								break;
							case 'H':
							case 'h':
								nsvg__pathHLineTo(p, ref cpx, ref cpy, args, cmd == 'h');
								cpx2 = cpx; cpy2 = cpy;
								break;
							case 'V':
							case 'v':
								nsvg__pathVLineTo(p, ref cpx, ref cpy, args, cmd == 'v');
								cpx2 = cpx; cpy2 = cpy;
								break;
							case 'C':
							case 'c':
								nsvg__pathCubicBezTo(p, ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 'c');
								break;
							case 'S':
							case 's':
								nsvg__pathCubicBezShortTo(p, ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 's');
								break;
							case 'Q':
							case 'q':
								nsvg__pathQuadBezTo(p, ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 'q');
								break;
							case 'T':
							case 't':
								nsvg__pathQuadBezShortTo(p, ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 't');
								break;
							case 'A':
							case 'a':
								nsvg__pathArcTo(p, ref cpx, ref cpy, args, cmd == 'a');
								cpx2 = cpx; cpy2 = cpy;
								break;
							default:
								if (nargs >= 2)
								{
									cpx = args[nargs - 2];
									cpy = args[nargs - 1];
									cpx2 = cpx; cpy2 = cpy;
								}
								break;
						}
						nargs = 0;
					}
				}
				else
				{
					cmd = item[0];
					if (cmd == 'M' || cmd == 'm')
					{
						// Commit path.
						if (p.npts > 0)
							nsvg__addPath(p, closedFlag);
						// Start new subpath.
						nsvg__resetPath(p);
						closedFlag = false;
						nargs = 0;
					}
					else if (initPoint == false)
					{
						// Do not allow other commands until initial point has been set (moveTo called once).
						cmd = '\0';
					}
					if (cmd == 'Z' || cmd == 'z')
					{
						closedFlag = true;
						// Commit path.
						if (p.npts > 0)
						{
							// Move current point to first point
							cpx = p.pts[0];
							cpy = p.pts[1];
							cpx2 = cpx; cpy2 = cpy;
							nsvg__addPath(p, closedFlag);
						}
						// Start new subpath.
						nsvg__resetPath(p);
						nsvg__moveTo(p, cpx, cpy);
						closedFlag = false;
						nargs = 0;
					}
					rargs = nsvg__getArgsPerElement(cmd);
					if (rargs == -1)
					{
						// Command not recognized
						cmd = '\0';
						rargs = 0;
					}
				}
			}
			// Commit path.
			if (p.npts > 0)
				nsvg__addPath(p, closedFlag);
		}

		nsvg__addShape(p);
	}

	static void nsvg__parseRect(NSVGparser p, ReadOnlySpan<string> attr)
	{
		float x = 0.0f;
		float y = 0.0f;
		float w = 0.0f;
		float h = 0.0f;
		float rx = -1.0f; // marks not set
		float ry = -1.0f;

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				var name = attr[i];
				var value = attr[i + 1].AsSpan();
				if (name == "x") x = nsvg__parseCoordinate(p, value, nsvg__actualOrigX(p), nsvg__actualWidth(p));
				if (name == "y") y = nsvg__parseCoordinate(p, value, nsvg__actualOrigY(p), nsvg__actualHeight(p));
				if (name == "width") w = nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualWidth(p));
				if (name == "height") h = nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualHeight(p));
				if (name == "rx") rx = MathF.Abs(nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualWidth(p)));
				if (name == "ry") ry = MathF.Abs(nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualHeight(p)));
			}
		}

		if (rx < 0.0f && ry > 0.0f) rx = ry;
		if (ry < 0.0f && rx > 0.0f) ry = rx;
		if (rx < 0.0f) rx = 0.0f;
		if (ry < 0.0f) ry = 0.0f;
		if (rx > w / 2.0f) rx = w / 2.0f;
		if (ry > h / 2.0f) ry = h / 2.0f;

		if (w != 0.0f && h != 0.0f)
		{
			nsvg__resetPath(p);

			if (rx < 0.00001f || ry < 0.0001f)
			{
				nsvg__moveTo(p, x, y);
				nsvg__lineTo(p, x + w, y);
				nsvg__lineTo(p, x + w, y + h);
				nsvg__lineTo(p, x, y + h);
			}
			else
			{
				// Rounded rectangle
				nsvg__moveTo(p, x + rx, y);
				nsvg__lineTo(p, x + w - rx, y);
				nsvg__cubicBezTo(p, x + w - rx * (1 - NSVG_KAPPA90), y, x + w, y + ry * (1 - NSVG_KAPPA90), x + w, y + ry);
				nsvg__lineTo(p, x + w, y + h - ry);
				nsvg__cubicBezTo(p, x + w, y + h - ry * (1 - NSVG_KAPPA90), x + w - rx * (1 - NSVG_KAPPA90), y + h, x + w - rx, y + h);
				nsvg__lineTo(p, x + rx, y + h);
				nsvg__cubicBezTo(p, x + rx * (1 - NSVG_KAPPA90), y + h, x, y + h - ry * (1 - NSVG_KAPPA90), x, y + h - ry);
				nsvg__lineTo(p, x, y + ry);
				nsvg__cubicBezTo(p, x, y + ry * (1 - NSVG_KAPPA90), x + rx * (1 - NSVG_KAPPA90), y, x + rx, y);
			}

			nsvg__addPath(p, true);

			nsvg__addShape(p);
		}
	}

	static void nsvg__parseCircle(NSVGparser p, ReadOnlySpan<string> attr)
	{
		float cx = 0.0f;
		float cy = 0.0f;
		float r = 0.0f;

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				var name = attr[i];
				var value = attr[i + 1].AsSpan();
				if (name == "cx") cx = nsvg__parseCoordinate(p, value, nsvg__actualOrigX(p), nsvg__actualWidth(p));
				if (name == "cy") cy = nsvg__parseCoordinate(p, value, nsvg__actualOrigY(p), nsvg__actualHeight(p));
				if (name == "r") r = MathF.Abs(nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualLength(p)));
			}
		}

		if (r > 0.0f)
		{
			nsvg__resetPath(p);

			nsvg__moveTo(p, cx + r, cy);
			nsvg__cubicBezTo(p, cx + r, cy + r * NSVG_KAPPA90, cx + r * NSVG_KAPPA90, cy + r, cx, cy + r);
			nsvg__cubicBezTo(p, cx - r * NSVG_KAPPA90, cy + r, cx - r, cy + r * NSVG_KAPPA90, cx - r, cy);
			nsvg__cubicBezTo(p, cx - r, cy - r * NSVG_KAPPA90, cx - r * NSVG_KAPPA90, cy - r, cx, cy - r);
			nsvg__cubicBezTo(p, cx + r * NSVG_KAPPA90, cy - r, cx + r, cy - r * NSVG_KAPPA90, cx + r, cy);

			nsvg__addPath(p, true);

			nsvg__addShape(p);
		}
	}

	static void nsvg__parseEllipse(NSVGparser p, ReadOnlySpan<string> attr)
	{
		float cx = 0.0f;
		float cy = 0.0f;
		float rx = 0.0f;
		float ry = 0.0f;

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				var name = attr[i];
				var value = attr[i + 1].AsSpan();
				if (name == "cx") cx = nsvg__parseCoordinate(p, value, nsvg__actualOrigX(p), nsvg__actualWidth(p));
				if (name == "cy") cy = nsvg__parseCoordinate(p, value, nsvg__actualOrigY(p), nsvg__actualHeight(p));
				if (name == "rx") rx = MathF.Abs(nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualWidth(p)));
				if (name == "ry") ry = MathF.Abs(nsvg__parseCoordinate(p, value, 0.0f, nsvg__actualHeight(p)));
			}
		}

		if (rx > 0.0f && ry > 0.0f)
		{
			nsvg__resetPath(p);

			nsvg__moveTo(p, cx + rx, cy);
			nsvg__cubicBezTo(p, cx + rx, cy + ry * NSVG_KAPPA90, cx + rx * NSVG_KAPPA90, cy + ry, cx, cy + ry);
			nsvg__cubicBezTo(p, cx - rx * NSVG_KAPPA90, cy + ry, cx - rx, cy + ry * NSVG_KAPPA90, cx - rx, cy);
			nsvg__cubicBezTo(p, cx - rx, cy - ry * NSVG_KAPPA90, cx - rx * NSVG_KAPPA90, cy - ry, cx, cy - ry);
			nsvg__cubicBezTo(p, cx + rx * NSVG_KAPPA90, cy - ry, cx + rx, cy - ry * NSVG_KAPPA90, cx + rx, cy);

			nsvg__addPath(p, true);

			nsvg__addShape(p);
		}
	}

	static void nsvg__parseLine(NSVGparser p, ReadOnlySpan<string> attr)
	{
		float x1 = 0.0f;
		float y1 = 0.0f;
		float x2 = 0.0f;
		float y2 = 0.0f;

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				var name = attr[i];
				var value = attr[i + 1].AsSpan();
				if (name == "x1") x1 = nsvg__parseCoordinate(p, value, nsvg__actualOrigX(p), nsvg__actualWidth(p));
				if (name == "y1") y1 = nsvg__parseCoordinate(p, value, nsvg__actualOrigY(p), nsvg__actualHeight(p));
				if (name == "x2") x2 = nsvg__parseCoordinate(p, value, nsvg__actualOrigX(p), nsvg__actualWidth(p));
				if (name == "y2") y2 = nsvg__parseCoordinate(p, value, nsvg__actualOrigY(p), nsvg__actualHeight(p));
			}
		}

		nsvg__resetPath(p);

		nsvg__moveTo(p, x1, y1);
		nsvg__lineTo(p, x2, y2);

		nsvg__addPath(p, false);

		nsvg__addShape(p);
	}

	static void nsvg__parsePoly(NSVGparser p, ReadOnlySpan<string> attr, bool closeFlag)
	{
		int npts = 0;
		Span<float> args = stackalloc float[2];
		int nargs = 0;

		nsvg__resetPath(p);

		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				if (attr[i] == "points")
				{
					var s = attr[i + 1].AsSpan();
					nargs = 0;
					while (s.Length > 0)
					{
						int n = nsvg__getNextPathItem(s, out var item);
						s = s[n..];
						if (item.Length == 0) break;
						args[nargs++] = (float)nsvg__atof(item);
						if (nargs >= 2)
						{
							if (npts == 0)
								nsvg__moveTo(p, args[0], args[1]);
							else
								nsvg__lineTo(p, args[0], args[1]);
							nargs = 0;
							npts++;
						}
					}
				}
			}
		}

		nsvg__addPath(p, closeFlag);

		nsvg__addShape(p);
	}

	static void nsvg__parseSVG(NSVGparser p, ReadOnlySpan<string> attr)
	{
		for (int i = 0; i < attr.Length; i += 2)
		{
			if (!nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan()))
			{
				var name = attr[i];
				var value = attr[i + 1].AsSpan();
				if (name == "width")
				{
					p.image.width = nsvg__parseCoordinate(p, value, 0.0f, 0.0f);
				}
				else if (name == "height")
				{
					p.image.height = nsvg__parseCoordinate(p, value, 0.0f, 0.0f);
				}
				else if (name == "viewBox")
				{
					var s = value;
					int n = nsvg__parseNumber(s, out var lit);
					s = s[n..];
					p.viewMinx = (float)nsvg__atof(lit);
					while (s.Length > 0 && (nsvg__isspace(s[0]) || s[0] == '%' || s[0] == ',')) s = s[1..];
					if (s.Length == 0) return;
					n = nsvg__parseNumber(s, out lit);
					s = s[n..];
					p.viewMiny = (float)nsvg__atof(lit);
					while (s.Length > 0 && (nsvg__isspace(s[0]) || s[0] == '%' || s[0] == ',')) s = s[1..];
					if (s.Length == 0) return;
					n = nsvg__parseNumber(s, out lit);
					s = s[n..];
					p.viewWidth = (float)nsvg__atof(lit);
					while (s.Length > 0 && (nsvg__isspace(s[0]) || s[0] == '%' || s[0] == ',')) s = s[1..];
					if (s.Length == 0) return;
					n = nsvg__parseNumber(s, out lit);
					s = s[n..];
					p.viewHeight = (float)nsvg__atof(lit);
				}
				else if (name == "preserveAspectRatio")
				{
					var v = value.ToString();
					if (v.Contains("none"))
					{
						// No uniform scaling
						p.alignType = NSVG_ALIGN_NONE;
					}
					else
					{
						// Parse X align
						if (v.Contains("xMin")) p.alignX = NSVG_ALIGN_MIN;
						else if (v.Contains("xMid")) p.alignX = NSVG_ALIGN_MID;
						else if (v.Contains("xMax")) p.alignX = NSVG_ALIGN_MAX;
						// Parse Y align
						if (v.Contains("yMin")) p.alignY = NSVG_ALIGN_MIN;
						else if (v.Contains("yMid")) p.alignY = NSVG_ALIGN_MID;
						else if (v.Contains("yMax")) p.alignY = NSVG_ALIGN_MAX;
						// Parse meet/slice
						p.alignType = NSVG_ALIGN_MEET;
						if (v.Contains("slice"))
							p.alignType = NSVG_ALIGN_SLICE;
					}
				}
			}
		}
	}

	static void nsvg__parseGradient(NSVGparser p, ReadOnlySpan<string> attr, sbyte type)
	{
		var grad = new NSVGgradientData();
		grad.units = (sbyte)NSVGgradientUnits.NSVG_OBJECT_SPACE;
		grad.type = type;
		if (grad.type == (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT)
		{
			grad.linear.x1 = nsvg__coord(0.0f, NSVGunits.NSVG_UNITS_PERCENT);
			grad.linear.y1 = nsvg__coord(0.0f, NSVGunits.NSVG_UNITS_PERCENT);
			grad.linear.x2 = nsvg__coord(100.0f, NSVGunits.NSVG_UNITS_PERCENT);
			grad.linear.y2 = nsvg__coord(0.0f, NSVGunits.NSVG_UNITS_PERCENT);
		}
		else if (grad.type == (sbyte)NSVGpaintType.NSVG_PAINT_RADIAL_GRADIENT)
		{
			grad.radial.cx = nsvg__coord(50.0f, NSVGunits.NSVG_UNITS_PERCENT);
			grad.radial.cy = nsvg__coord(50.0f, NSVGunits.NSVG_UNITS_PERCENT);
			grad.radial.r = nsvg__coord(50.0f, NSVGunits.NSVG_UNITS_PERCENT);
		}

		nsvg__xformIdentity(ref grad.xform);

		for (int i = 0; i < attr.Length; i += 2)
		{
			var name = attr[i];
			var value = attr[i + 1].AsSpan();
			if (name == "id")
			{
				grad.id = new string(value);
			}
			else if (!nsvg__parseAttr(p, name, value))
			{
				if (name == "gradientUnits")
				{
					if (value is "objectBoundingBox")
						grad.units = (sbyte)NSVGgradientUnits.NSVG_OBJECT_SPACE;
					else
						grad.units = (sbyte)NSVGgradientUnits.NSVG_USER_SPACE;
				}
				else if (name == "gradientTransform")
				{
					nsvg__parseTransform(ref grad.xform, value);
				}
				else if (name == "cx") grad.radial.cx = nsvg__parseCoordinateRaw(value);
				else if (name == "cy") grad.radial.cy = nsvg__parseCoordinateRaw(value);
				else if (name == "r") grad.radial.r = nsvg__parseCoordinateRaw(value);
				else if (name == "fx") grad.radial.fx = nsvg__parseCoordinateRaw(value);
				else if (name == "fy") grad.radial.fy = nsvg__parseCoordinateRaw(value);
				else if (name == "x1") grad.linear.x1 = nsvg__parseCoordinateRaw(value);
				else if (name == "y1") grad.linear.y1 = nsvg__parseCoordinateRaw(value);
				else if (name == "x2") grad.linear.x2 = nsvg__parseCoordinateRaw(value);
				else if (name == "y2") grad.linear.y2 = nsvg__parseCoordinateRaw(value);
				else if (name == "spreadMethod")
				{
					if (value is "pad") grad.spread = (sbyte)NSVGspreadType.NSVG_SPREAD_PAD;
					else if (value is "reflect") grad.spread = (sbyte)NSVGspreadType.NSVG_SPREAD_REFLECT;
					else if (value is "repeat") grad.spread = (sbyte)NSVGspreadType.NSVG_SPREAD_REPEAT;
				}
				else if (name == "xlink:href")
				{
					var href = value;
					if (href.Length > 0 && href[0] == '#') href = href[1..];
					grad.@ref = new string(href);
				}
			}
		}

		p.gradients.Add(grad);
	}

	static void nsvg__parseGradientStop(NSVGparser p, ReadOnlySpan<string> attr)
	{
		ref NSVGattrib curAttr = ref nsvg__getAttr(p);

		curAttr.stopOffset = 0;
		curAttr.stopColor = 0;
		curAttr.stopOpacity = 1.0f;

		for (int i = 0; i < attr.Length; i += 2)
		{
			nsvg__parseAttr(p, attr[i].AsSpan(), attr[i + 1].AsSpan());
		}

		// Add stop to the last gradient.
		if (p.gradients.Count == 0) return;
		var grad = p.gradients[^1];

		// Insert the new stop sorted by offset.
		int idx = 0;
		while (idx < grad.stops.Count && curAttr.stopOffset >= grad.stops[idx].offset)
			idx++;

		var stop = new NSVGgradientStop();
		stop.color = curAttr.stopColor;
		stop.color |= (uint)(curAttr.stopOpacity * 255) << 24;
		stop.offset = curAttr.stopOffset;

		grad.stops.Insert(idx, stop);
		grad.nstops = grad.stops.Count;
		p.gradients[^1] = grad;
	}

	static void nsvg__startElement(object ud, ReadOnlySpan<char> el, ReadOnlySpan<string> attr)
	{
		var p = (NSVGparser)ud;

		if (p.defsFlag != 0)
		{
			// Skip everything but gradients and styles in defs
			if (el is "linearGradient")
				nsvg__parseGradient(p, attr, (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT);
			else if (el is "radialGradient")
				nsvg__parseGradient(p, attr, (sbyte)NSVGpaintType.NSVG_PAINT_RADIAL_GRADIENT);
			else if (el is "stop")
				nsvg__parseGradientStop(p, attr);
			else if (el is "style")
				p.styleFlag = 1;
			return;
		}

		if (el is "g")
		{
			nsvg__pushAttr(p);
			nsvg__parseAttribs(p, attr);
		}
		else if (el is "path")
		{
			if (p.pathFlag != 0)	// Do not allow nested paths.
				return;
			nsvg__pushAttr(p);
			nsvg__parsePath(p, attr);
			nsvg__popAttr(p);
		}
		else if (el is "rect")
		{
			nsvg__pushAttr(p);
			nsvg__parseRect(p, attr);
			nsvg__popAttr(p);
		}
		else if (el is "circle")
		{
			nsvg__pushAttr(p);
			nsvg__parseCircle(p, attr);
			nsvg__popAttr(p);
		}
		else if (el is "ellipse")
		{
			nsvg__pushAttr(p);
			nsvg__parseEllipse(p, attr);
			nsvg__popAttr(p);
		}
		else if (el is "line")
		{
			nsvg__pushAttr(p);
			nsvg__parseLine(p, attr);
			nsvg__popAttr(p);
		}
		else if (el is "polyline")
		{
			nsvg__pushAttr(p);
			nsvg__parsePoly(p, attr, false);
			nsvg__popAttr(p);
		}
		else if (el is "polygon")
		{
			nsvg__pushAttr(p);
			nsvg__parsePoly(p, attr, true);
			nsvg__popAttr(p);
		}
		else if (el is "linearGradient")
		{
			nsvg__parseGradient(p, attr, (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT);
		}
		else if (el is "radialGradient")
		{
			nsvg__parseGradient(p, attr, (sbyte)NSVGpaintType.NSVG_PAINT_RADIAL_GRADIENT);
		}
		else if (el is "stop")
		{
			nsvg__parseGradientStop(p, attr);
		}
		else if (el is "defs")
		{
			p.defsFlag = 1;
		}
		else if (el is "svg")
		{
			nsvg__parseSVG(p, attr);
		}
		else if (el is "style")
		{
			p.styleFlag = 1;
		}
	}

	static void nsvg__endElement(object ud, ReadOnlySpan<char> el)
	{
		var p = (NSVGparser)ud;

		if (el is "g")
		{
			nsvg__popAttr(p);
		}
		else if (el is "path")
		{
			p.pathFlag = 0;
		}
		else if (el is "defs")
		{
			p.defsFlag = 0;
		}
		else if (el is "style")
		{
			p.styleFlag = 0;
		}
	}

	static void nsvg__content(object ud, ReadOnlySpan<char> s)
	{
		var p = (NSVGparser)ud;
		if (p.styleFlag == 0)
			return;

		// Parse all the styles inside the style block. Each style's content will be later
		// processed using nsvg__parseStyle(). Note: We only support selector lists of
		// simple class selectors (e.g. ".foo, .bar { ... }").
		while (s.Length > 0)
		{
			var staging = new List<NSVGstyleDeclaration>();

			// 1) Parse the selector list up to '{'. For each simple class selector ('.name'),
			//    stage a new NSVGstyleDeclaration.
			while (s.Length > 0 && s[0] != '{')
			{
				while (s.Length > 0 && (nsvg__isspace(s[0]) || s[0] == ','))
					s = s[1..];
				if (s.Length == 0 || s[0] == '{')
					break;

				int selLen = 0;
				while (selLen < s.Length && !nsvg__isspace(s[selLen]) && s[selLen] != ',' && s[selLen] != '{')
					selLen++;
				var sel = s[..selLen];
				s = s[selLen..];

				if (sel.Length == 0 || sel[0] != '.' || staging.Count >= NSVG_MAX_CLASSES)
					continue; // unsupported selector, or staging array full
				staging.Add(new NSVGstyleDeclaration { className = new string(sel[1..]) });
			}
			if (s.Length == 0)
			{
				// No '{' found - discard pending styles and stop.
				break;
			}
			s = s[1..]; // advance past '{'

			// 2) Find the end of the properties block (up to '}').
			int propsLen = 0;
			while (propsLen < s.Length && s[propsLen] != '}')
				propsLen++;
			var propsText = new string(s[..propsLen]);
			s = s[propsLen..];

			// 3) Commit styles (head-insertion, mirroring the C linked list order).
			for (int i = staging.Count - 1; i >= 0; i--)
			{
				var style = staging[i];
				style.propertiesText = propsText;
				p.styles.Insert(0, style);
			}

			if (s.Length > 0)
				s = s[1..]; // advance past '}'
		}
	}

	static float nsvg__getAverageScale(InlineArray6<float> t)
	{
		float sx = MathF.Sqrt(t[0] * t[0] + t[2] * t[2]);
		float sy = MathF.Sqrt(t[1] * t[1] + t[3] * t[3]);
		return (sx + sy) * 0.5f;
	}

	static void nsvg__getLocalBounds(ref InlineArray4<float> bounds, NSVGshape shape, InlineArray6<float> xform)
	{
		float[] curve = new float[4 * 2];
		InlineArray4<float> curveBounds = default;
		bool first = true;
		foreach (var path in shape.paths)
		{
			nsvg__xformPoint(ref curve[0], ref curve[1], path.pts[0], path.pts[1], xform);
			for (int i = 0; i < path.npts - 1; i += 3)
			{
				nsvg__xformPoint(ref curve[2], ref curve[3], path.pts[(i + 1) * 2], path.pts[(i + 1) * 2 + 1], xform);
				nsvg__xformPoint(ref curve[4], ref curve[5], path.pts[(i + 2) * 2], path.pts[(i + 2) * 2 + 1], xform);
				nsvg__xformPoint(ref curve[6], ref curve[7], path.pts[(i + 3) * 2], path.pts[(i + 3) * 2 + 1], xform);
				nsvg__curveBounds(ref curveBounds, curve);
				if (first)
				{
					bounds[0] = curveBounds[0];
					bounds[1] = curveBounds[1];
					bounds[2] = curveBounds[2];
					bounds[3] = curveBounds[3];
					first = false;
				}
				else
				{
					bounds[0] = nsvg__minf(bounds[0], curveBounds[0]);
					bounds[1] = nsvg__minf(bounds[1], curveBounds[1]);
					bounds[2] = nsvg__maxf(bounds[2], curveBounds[2]);
					bounds[3] = nsvg__maxf(bounds[3], curveBounds[3]);
				}
				curve[0] = curve[6];
				curve[1] = curve[7];
			}
		}
	}

	static void nsvg__addShape(NSVGparser p)
	{
		ref NSVGattrib attr = ref nsvg__getAttr(p);
		float scale;

		if (p.plist.Count == 0)
			return;

		var shape = new NSVGshape();

		shape.id = attr.id;
		shape.fillGradient = attr.fillGradient;
		shape.strokeGradient = attr.strokeGradient;
		shape.xform = attr.xform;
		scale = nsvg__getAverageScale(attr.xform);
		shape.strokeWidth = attr.strokeWidth * scale;
		shape.strokeDashOffset = attr.strokeDashOffset * scale;
		shape.strokeDashCount = (sbyte)attr.strokeDashCount;
		for (int i = 0; i < attr.strokeDashCount; i++)
			shape.strokeDashArray[i] = attr.strokeDashArray[i] * scale;
		shape.strokeLineJoin = (sbyte)attr.strokeLineJoin;
		shape.strokeLineCap = (sbyte)attr.strokeLineCap;
		shape.miterLimit = attr.miterLimit;
		shape.fillRule = (sbyte)attr.fillRule;
		shape.opacity = attr.opacity;
		shape.paintOrder = attr.paintOrder;

		shape.paths = p.plist;
		p.plist = new List<NSVGpath>();

		// Calculate shape bounds
		shape.bounds[0] = shape.paths[0].bounds[0];
		shape.bounds[1] = shape.paths[0].bounds[1];
		shape.bounds[2] = shape.paths[0].bounds[2];
		shape.bounds[3] = shape.paths[0].bounds[3];
		for (int k = 1; k < shape.paths.Count; k++)
		{
			var path = shape.paths[k];
			shape.bounds[0] = nsvg__minf(shape.bounds[0], path.bounds[0]);
			shape.bounds[1] = nsvg__minf(shape.bounds[1], path.bounds[1]);
			shape.bounds[2] = nsvg__maxf(shape.bounds[2], path.bounds[2]);
			shape.bounds[3] = nsvg__maxf(shape.bounds[3], path.bounds[3]);
		}

		// Set fill
		if (attr.hasFill == 0)
		{
			shape.fill.type = (sbyte)NSVGpaintType.NSVG_PAINT_NONE;
		}
		else if (attr.hasFill == 1)
		{
			shape.fill.type = (sbyte)NSVGpaintType.NSVG_PAINT_COLOR;
			shape.fill.color = attr.fillColor;
			shape.fill.color |= (uint)(attr.fillOpacity * 255) << 24;
		}
		else if (attr.hasFill == 2)
		{
			shape.fill.type = (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF;
		}

		// Set stroke
		if (attr.hasStroke == 0)
		{
			shape.stroke.type = (sbyte)NSVGpaintType.NSVG_PAINT_NONE;
		}
		else if (attr.hasStroke == 1)
		{
			shape.stroke.type = (sbyte)NSVGpaintType.NSVG_PAINT_COLOR;
			shape.stroke.color = attr.strokeColor;
			shape.stroke.color |= (uint)(attr.strokeOpacity * 255) << 24;
		}
		else if (attr.hasStroke == 2)
		{
			shape.stroke.type = (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF;
		}

		// Set flags
		shape.flags = (byte)(attr.visible != 0 ? (int)NSVGflags.NSVG_FLAGS_VISIBLE : 0);

		// Add to tail (List preserves document order like the C tail insertion)
		p.image.shapes.Add(shape);
	}

	static void nsvg__addPath(NSVGparser p, bool closed)
	{
		ref NSVGattrib attr = ref nsvg__getAttr(p);

		if (p.npts < 4)
			return;

		if (closed)
			nsvg__lineTo(p, p.pts[0], p.pts[1]);

		// Expect 1 + N*3 points (N = number of cubic bezier segments).
		if ((p.npts % 3) != 1)
			return;

		var path = new NSVGpath();
		path.pts = new float[p.npts * 2];
		path.closed = (sbyte)(closed ? 1 : 0);
		path.npts = p.npts;

		// Transform path.
		for (int i = 0; i < p.npts; ++i)
			nsvg__xformPoint(ref path.pts[i * 2], ref path.pts[i * 2 + 1], p.pts[i * 2], p.pts[i * 2 + 1], attr.xform);

		// Find bounds
		for (int i = 0; i < path.npts - 1; i += 3)
		{
			InlineArray4<float> bounds = default;
			nsvg__curveBounds(ref bounds, path.pts.AsSpan(i * 2, 8));
			if (i == 0)
			{
				path.bounds[0] = bounds[0];
				path.bounds[1] = bounds[1];
				path.bounds[2] = bounds[2];
				path.bounds[3] = bounds[3];
			}
			else
			{
				path.bounds[0] = nsvg__minf(path.bounds[0], bounds[0]);
				path.bounds[1] = nsvg__minf(path.bounds[1], bounds[1]);
				path.bounds[2] = nsvg__maxf(path.bounds[2], bounds[2]);
				path.bounds[3] = nsvg__maxf(path.bounds[3], bounds[3]);
			}
		}

		// Head-insert to mirror the C linked list (paths within a shape are in reverse parse order).
		p.plist.Insert(0, path);
	}

	static NSVGgradient? nsvg__createGradient(NSVGparser p, string id, ReadOnlySpan<float> localBounds, InlineArray6<float> xform, out sbyte paintType)
	{
		var data = nsvg__findGradientData(p, id);
		if (data == null)
		{
			paintType = 0;
			return null;
		}

		NSVGgradientStop[]? stops = null;
		int nstops = 0;
		var refData = data;
		int refIter = 0;
		while (refData != null)
		{
			if (stops == null && refData.stops != null)
			{
				stops = refData.stops.ToArray();
				nstops = refData.nstops;
				break;
			}
			var nextRef = nsvg__findGradientData(p, refData.@ref);
			if (nextRef == refData) break; // prevent infinite loops on malformed data
			refData = nextRef;
			refIter++;
			if (refIter > 32) break;
		}
		if (stops == null)
		{
			paintType = 0;
			return null;
		}

		var grad = new NSVGgradient();

		// The shape width and height.
		float ox, oy, sw, sh;
		if (data.units == (sbyte)NSVGgradientUnits.NSVG_OBJECT_SPACE)
		{
			ox = localBounds[0];
			oy = localBounds[1];
			sw = localBounds[2] - localBounds[0];
			sh = localBounds[3] - localBounds[1];
		}
		else
		{
			ox = nsvg__actualOrigX(p);
			oy = nsvg__actualOrigY(p);
			sw = nsvg__actualWidth(p);
			sh = nsvg__actualHeight(p);
		}
		float sl = MathF.Sqrt(sw * sw + sh * sh) / MathF.Sqrt(2.0f);

		if (data.type == (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT)
		{
			float x1, y1, x2, y2, dx, dy;
			x1 = nsvg__convertToPixels(p, data.linear.x1, ox, sw);
			y1 = nsvg__convertToPixels(p, data.linear.y1, oy, sh);
			x2 = nsvg__convertToPixels(p, data.linear.x2, ox, sw);
			y2 = nsvg__convertToPixels(p, data.linear.y2, oy, sh);
			// Calculate transform aligned to the line
			dx = x2 - x1;
			dy = y2 - y1;
			grad.xform[0] = dy; grad.xform[1] = -dx;
			grad.xform[2] = dx; grad.xform[3] = dy;
			grad.xform[4] = x1; grad.xform[5] = y1;
		}
		else
		{
			float cx, cy, fx, fy, r;
			cx = nsvg__convertToPixels(p, data.radial.cx, ox, sw);
			cy = nsvg__convertToPixels(p, data.radial.cy, oy, sh);
			fx = nsvg__convertToPixels(p, data.radial.fx, ox, sw);
			fy = nsvg__convertToPixels(p, data.radial.fy, oy, sh);
			r = nsvg__convertToPixels(p, data.radial.r, 0, sl);
			// Calculate transform aligned to the circle
			grad.xform[0] = r; grad.xform[1] = 0;
			grad.xform[2] = 0; grad.xform[3] = r;
			grad.xform[4] = cx; grad.xform[5] = cy;
			grad.fx = fx / r;
			grad.fy = fy / r;
		}

		nsvg__xformMultiply(ref grad.xform, in data.xform);
		nsvg__xformMultiply(ref grad.xform, in xform);

		grad.spread = data.spread;
		grad.stops = stops;
		grad.nstops = nstops;

		paintType = data.type;

		return grad;
	}

	static void nsvg__createGradients(NSVGparser p)
	{
		var shapes = p.image.shapes;
		for (int s = 0; s < shapes.Count; s++)
		{
			var shape = shapes[s];
			if (shape.fill.type == (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF)
			{
				if (shape.fillGradient.Length != 0)
				{
					InlineArray6<float> inv = default;
					InlineArray4<float> localBounds = default;
					nsvg__xformInverse(ref inv, ref shape.xform);
					nsvg__getLocalBounds(ref localBounds, shape, inv);
					shape.fill.gradient = nsvg__createGradient(p, shape.fillGradient, localBounds, shape.xform, out shape.fill.type);
				}
				if (shape.fill.type == (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF)
				{
					shape.fill.type = (sbyte)NSVGpaintType.NSVG_PAINT_NONE;
				}
			}
			if (shape.stroke.type == (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF)
			{
				if (shape.strokeGradient.Length != 0)
				{
					InlineArray6<float> inv = default;
					InlineArray4<float> localBounds = default;
					nsvg__xformInverse(ref inv, ref shape.xform);
					nsvg__getLocalBounds(ref localBounds, shape, inv);
					shape.stroke.gradient = nsvg__createGradient(p, shape.strokeGradient, localBounds, shape.xform, out shape.stroke.type);
				}
				if (shape.stroke.type == (sbyte)NSVGpaintType.NSVG_PAINT_UNDEF)
				{
					shape.stroke.type = (sbyte)NSVGpaintType.NSVG_PAINT_NONE;
				}
			}
			shapes[s] = shape;
		}
	}

	static void nsvg__imageBounds(NSVGparser p, ref InlineArray4<float> bounds)
	{
		if (p.image.shapes.Count == 0)
		{
			bounds[0] = bounds[1] = bounds[2] = bounds[3] = 0.0f;
			return;
		}
		bounds[0] = p.image.shapes[0].bounds[0];
		bounds[1] = p.image.shapes[0].bounds[1];
		bounds[2] = p.image.shapes[0].bounds[2];
		bounds[3] = p.image.shapes[0].bounds[3];
		for (int i = 1; i < p.image.shapes.Count; i++)
		{
			var shape = p.image.shapes[i];
			bounds[0] = nsvg__minf(bounds[0], shape.bounds[0]);
			bounds[1] = nsvg__minf(bounds[1], shape.bounds[1]);
			bounds[2] = nsvg__maxf(bounds[2], shape.bounds[2]);
			bounds[3] = nsvg__maxf(bounds[3], shape.bounds[3]);
		}
	}

	static float nsvg__viewAlign(float content, float container, int type)
	{
		if (type == NSVG_ALIGN_MIN)
			return 0;
		if (type == NSVG_ALIGN_MAX)
			return container - content;
		// mid
		return (container - content) * 0.5f;
	}

	static void nsvg__scaleGradient(ref NSVGgradient grad, float tx, float ty, float sx, float sy)
	{
		InlineArray6<float> t = default;
		nsvg__xformSetTranslation(ref t, tx, ty);
		nsvg__xformMultiply(ref grad.xform, in t);

		nsvg__xformSetScale(ref t, sx, sy);
		nsvg__xformMultiply(ref grad.xform, in t);
	}

	static void nsvg__scaleToViewbox(NSVGparser p, ReadOnlySpan<char> units)
	{
		InlineArray4<float> bounds = default;

		// Guess image size if not set completely.
		nsvg__imageBounds(p, ref bounds);

		if (p.viewWidth == 0)
		{
			if (p.image.width > 0)
			{
				p.viewWidth = p.image.width;
			}
			else
			{
				p.viewMinx = bounds[0];
				p.viewWidth = bounds[2] - bounds[0];
			}
		}
		if (p.viewHeight == 0)
		{
			if (p.image.height > 0)
			{
				p.viewHeight = p.image.height;
			}
			else
			{
				p.viewMiny = bounds[1];
				p.viewHeight = bounds[3] - bounds[1];
			}
		}
		if (p.image.width == 0)
			p.image.width = p.viewWidth;
		if (p.image.height == 0)
			p.image.height = p.viewHeight;

		float tx = -p.viewMinx;
		float ty = -p.viewMiny;
		float sx = p.viewWidth > 0 ? p.image.width / p.viewWidth : 0;
		float sy = p.viewHeight > 0 ? p.image.height / p.viewHeight : 0;
		// Unit scaling
		float us = 1.0f / nsvg__convertToPixels(p, nsvg__coord(1.0f, nsvg__parseUnits(units)), 0.0f, 1.0f);

		// Fix aspect ratio
		if (p.alignType == NSVG_ALIGN_MEET)
		{
			// fit whole image into viewbox
			sx = sy = nsvg__minf(sx, sy);
			tx += nsvg__viewAlign(p.viewWidth * sx, p.image.width, p.alignX) / sx;
			ty += nsvg__viewAlign(p.viewHeight * sy, p.image.height, p.alignY) / sy;
		}
		else if (p.alignType == NSVG_ALIGN_SLICE)
		{
			// fill whole viewbox with image
			sx = sy = nsvg__maxf(sx, sy);
			tx += nsvg__viewAlign(p.viewWidth * sx, p.image.width, p.alignX) / sx;
			ty += nsvg__viewAlign(p.viewHeight * sy, p.image.height, p.alignY) / sy;
		}

		// Transform
		sx *= us;
		sy *= us;
		float avgs = (sx + sy) / 2.0f;

		var shapes = p.image.shapes;
		for (int si = 0; si < shapes.Count; si++)
		{
			var shape = shapes[si];
			shape.bounds[0] = (shape.bounds[0] + tx) * sx;
			shape.bounds[1] = (shape.bounds[1] + ty) * sy;
			shape.bounds[2] = (shape.bounds[2] + tx) * sx;
			shape.bounds[3] = (shape.bounds[3] + ty) * sy;
			for (int pi = 0; pi < shape.paths.Count; pi++)
			{
				var path = shape.paths[pi];
				path.bounds[0] = (path.bounds[0] + tx) * sx;
				path.bounds[1] = (path.bounds[1] + ty) * sy;
				path.bounds[2] = (path.bounds[2] + tx) * sx;
				path.bounds[3] = (path.bounds[3] + ty) * sy;
				for (int i = 0; i < path.npts; i++)
				{
					path.pts[i * 2] = (path.pts[i * 2] + tx) * sx;
					path.pts[i * 2 + 1] = (path.pts[i * 2 + 1] + ty) * sy;
				}
				shape.paths[pi] = path;
			}

			if (shape.fill.type == (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT || shape.fill.type == (sbyte)NSVGpaintType.NSVG_PAINT_RADIAL_GRADIENT)
			{
				if (shape.fill.gradient is { } fg)
				{
					nsvg__scaleGradient(ref fg, tx, ty, sx, sy);
					InlineArray6<float> t = default;
					memcpy<float>(t, fg.xform, 6);
					nsvg__xformInverse(ref fg.xform, ref t);
					shape.fill.gradient = fg;
				}
			}
			if (shape.stroke.type == (sbyte)NSVGpaintType.NSVG_PAINT_LINEAR_GRADIENT || shape.stroke.type == (sbyte)NSVGpaintType.NSVG_PAINT_RADIAL_GRADIENT)
			{
				if (shape.stroke.gradient is { } sg)
				{
					nsvg__scaleGradient(ref sg, tx, ty, sx, sy);
					InlineArray6<float> t = default;
					memcpy<float>(t, sg.xform, 6);
					nsvg__xformInverse(ref sg.xform, ref t);
					shape.stroke.gradient = sg;
				}
			}

			shape.strokeWidth *= avgs;
			shape.strokeDashOffset *= avgs;
			for (int i = 0; i < shape.strokeDashCount; i++)
				shape.strokeDashArray[i] *= avgs;
			shapes[si] = shape;
		}
	}

	/// <summary>
	/// Parses an SVG document from a string and returns the parsed image as cubic bezier shapes.
	/// The input string is copied before parsing (the XML parser mutates the buffer in place).
	/// </summary>
	/// <param name="input">SVG document text.</param>
	/// <param name="units">Output unit: "px", "pt", "pc", "mm", "cm", "in", "%", "em" or "ex".</param>
	/// <param name="dpi">Dots-per-inch used for unit conversion (e.g. 96).</param>
	public static NSVGimage Parse(string input, string units, float dpi)
	{
		var buffer = input.ToCharArray();
		var p = nsvg__createParser();
		p.dpi = dpi;

		nsvg__parseXML(buffer, nsvg__startElement, nsvg__endElement, nsvg__content, p);

		// Create gradients after all definitions have been parsed
		nsvg__createGradients(p);

		// Scale to viewBox
		nsvg__scaleToViewbox(p, units);

		return p.image;
	}

	/// <summary>Parses an SVG file and returns the parsed image as cubic bezier shapes.</summary>
	public static NSVGimage ParseFromFile(string path, string units, float dpi)
	{
		return Parse(File.ReadAllText(path), units, dpi);
	}

	/// <summary>Deep-copies a parsed path.</summary>
	public static NSVGpath DuplicatePath(NSVGpath path)
	{
		var res = new NSVGpath();
		res.pts = new float[path.npts * 2];
		path.pts.CopyTo(res.pts, 0);
		res.npts = path.npts;
		res.bounds = path.bounds;
		res.closed = path.closed;
		return res;
	}
}

[InlineArray(128)]
public struct InlineArray128<T>
{
#nullable disable
	private T t;
}
