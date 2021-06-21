/*
* unlzexe ver 0.5 (PC-VAN UTJ44266 Kou )
*   UNLZEXE converts the compressed file by lzexe(ver.0.90,0.91) to the
*   UNcompressed executable one.
*
*   usage:  UNLZEXE packedfile[.EXE] [unpackedfile.EXE]

v0.6  David Kirschbaum, Toad Hall, kirsch@usasoc.soc.mil, Jul 91
	Problem reported by T.Salmi (ts@uwasa.fi) with UNLZEXE when run
	with TLB-V119 on 386's.
	Stripping out the iskanji and isjapan() stuff (which uses a somewhat
	unusual DOS interrupt) to see if that's what's biting us.

--  Found it, thanks to Dan Lewis (DLEWIS@SCUACC.SCU.EDU).
	Silly us:  didn't notice the "r.h.al=0x3800;" in isjapan().
	Oh, you don't see it either?  INT functions are called with AH
	having the service.  Changing to "r.x.ax=0x3800;".

v0.7  Alan Modra, amodra@sirius.ucs.adelaide.edu.au, Nov 91
    Fixed problem with large files by casting ihead components to long
    in various expressions.
    Fixed MinBSS & MaxBSS calculation (ohead[5], ohead[6]).  Now UNLZEXE
    followed by LZEXE should give the original file.

v0.8  Vesselin Bontchev, bontchev@fbihh.informatik.uni-hamburg.de, Aug 92
    Fixed recognition of EXE files - both 'MZ' and 'ZM' in the header
    are recognized.
    Recognition of compressed files made more robust - now just
    patching the 'LZ90' and 'LZ91' strings will not fool the program.

v0.9  Stian Skjelstad, stian.skjelstad@gmail.com, Aug 2019
    Use memmove when memory-regions overlap
    Do not use putw/getw, since they on modern systems do not read/write 16bit
    getc() return char, which might be signed.
    Include POSIX headers
    span + pointer, was expected to wrap 16bit
*/

using System;
using System.IO;
using System.Linq;
using WORD = System.UInt16;
using BYTE = System.Byte;
using System.Runtime.InteropServices;

static class Program
{
    const int FAILURE = 1;
    const int SUCCESS = 0;

    const int EXIT_FAILURE = 1;

    static string tmpfname = "$tmpfil$.exe";
    static string backup_ext = ".olz";
    static string ipath,
         opath,
         ofname;

    static int Main(string[] argv){
        var argc = argv.Length;
        Stream ifile, ofile;
        int ver;
        bool rename_sw =false;

        Console.WriteLine("UNLZEXE Ver. 0.6");             /* v0.6 */
        if(argc!=2 && argc!=1){
            Console.WriteLine("usage: UNLZEXE packedfile [unpackedfile]");
            return EXIT_FAILURE;
        }
        if(argc==1)
            rename_sw=true;
        if(fnamechk(out ipath, out opath, out ofname, argc,argv)!=SUCCESS) {
            return EXIT_FAILURE;
        }

        try{
            ifile = File.Open(ipath, FileMode.Open, FileAccess.Read);
        }catch{
            Console.WriteLine($"'{ipath}' :not found");
            return EXIT_FAILURE;
        }

        if(rdhead(ifile,out ver)!=SUCCESS){
            Console.WriteLine($"'{ipath}' is not LZEXE file.");
            ifile.Close();
            return EXIT_FAILURE;
        }
        try{
            ofile = File.Open(opath, FileMode.Create, FileAccess.Write);
        }catch{
            Console.WriteLine($"can't open '{opath}'.");
            ifile.Close();
            return EXIT_FAILURE;
        }
        Console.WriteLine($"file '{ipath}' is compressed by LZEXE Ver. 0.{ver}"); /* v0.8 */
        var ireader = new BinaryReader(ifile);
        var owriter = new BinaryWriter(ofile);
        if(mkreltbl(ireader, owriter, ver)!=SUCCESS) {
            ifile.Close();
            ofile.Close();
            File.Delete(opath);
            return EXIT_FAILURE;
        }
        if(unpack(ireader, owriter) !=SUCCESS) {
            ifile.Close();
            ofile.Close();
            File.Delete(opath);
            return EXIT_FAILURE;
        }
        ifile.Close();
        wrhead(owriter);
        ofile.Close();

        if(fnamechg(ipath,opath,ofname,rename_sw)!=SUCCESS){
            return EXIT_FAILURE;
        }
        return 0;
    }

    /* file name check */
    static int fnamechk(out string ipath,out string opath, out string ofname,
                  int argc,string[] argv) {
        int idx_name,idx_ext;

        ipath = argv[0];
        parsepath(ipath,out idx_name,out idx_ext);
        if (idx_ext >= ipath.Length) ipath = ipath.Substring(0, idx_ext) + ".exe";
        if(tmpfname.Equals(ipath+idx_name,StringComparison.OrdinalIgnoreCase)){
            Console.WriteLine($"'{ipath}':bad filename.");
            opath = null;
            ofname = null;
            return FAILURE;
        }
        if(argc==1)
            opath = ipath;
        else
            opath = argv[1];
        parsepath(opath,out idx_name,out idx_ext);
        if (idx_ext >= opath.Length) opath = opath.Substring(0, idx_ext) + ".exe";
        if (backup_ext.Equals(opath+idx_ext, StringComparison.OrdinalIgnoreCase)){
            Console.WriteLine($"'{opath}':bad filename.");
            ofname = null;
            return FAILURE;
        }
        ofname = opath.Substring(idx_name);
        opath = opath.Substring(0, idx_name) + tmpfname;
        return SUCCESS;
    }


    static int fnamechg(string ipath,string opath,string ofname,bool rename_sw) {
        int idx_name,idx_ext;
        string tpath;

        if(rename_sw) {
            tpath = ipath;
            parsepath(tpath,out idx_name,out idx_ext);
            tpath = tpath.Substring(0, idx_ext) + backup_ext;
            File.Delete(tpath);
            try{
                File.Move(ipath, tpath);
            }catch{
                Console.WriteLine($"can't make '{tpath}'.");
                File.Delete(opath);
                return FAILURE;
            }
	    Console.WriteLine($"'{ipath}' is renamed to '{tpath}'.");
        }
        tpath = opath;
        parsepath(tpath,out idx_name,out idx_ext);
        tpath = tpath.Substring(0, idx_name) + ofname;
        File.Delete(tpath);
        try{
            File.Move(opath, tpath);
        }catch{
            if(rename_sw) {
                tpath = ipath;
                parsepath(tpath,out idx_name,out idx_ext);
                tpath = tpath.Substring(0, idx_ext) + backup_ext;
                File.Move(tpath,ipath);
            }
            Console.WriteLine($"can't make '{tpath}'.  unpacked file '{tmpfname}' is remained.");

            return FAILURE;
        }
        Console.WriteLine($"unpacked file '{tpath}' is generated.");
        return SUCCESS;
    }

    static void parsepath(string pathname, out int fname, out int ext) {
        /* use  int japan_f */
        int i;

        fname=0; ext=0;
        for(i=0;i < pathname.Length; i++) {
            switch(pathname[i]) {
            case ':' :
            case '\\':  fname=i+1; break;
            case '.' :  ext=i; break;
            }
        }
        if(ext<=fname) ext=i;
    }
    /*-------------------------------------------*/
    static BYTE[] ihead_buffer = new BYTE[0x10 * sizeof(WORD)], ohead_buffer = new BYTE[0x10 * sizeof(WORD)], inf_buffer = new BYTE[8 * sizeof(WORD)];
    static Span<WORD> ihead => MemoryMarshal.Cast<BYTE, WORD>(ihead_buffer.AsSpan());
    static Span<WORD> ohead => MemoryMarshal.Cast<BYTE, WORD>(ohead_buffer.AsSpan());
    static Span<WORD> inf => MemoryMarshal.Cast<BYTE, WORD>(inf_buffer.AsSpan());
    static long loadsize;
    static BYTE[] sig90 = {			/* v0.8 */
        0x06, 0x0E, 0x1F, 0x8B, 0x0E, 0x0C, 0x00, 0x8B,
        0xF1, 0x4E, 0x89, 0xF7, 0x8C, 0xDB, 0x03, 0x1E,
        0x0A, 0x00, 0x8E, 0xC3, 0xB4, 0x00, 0x31, 0xED,
        0xFD, 0xAC, 0x01, 0xC5, 0xAA, 0xE2, 0xFA, 0x8B,
        0x16, 0x0E, 0x00, 0x8A, 0xC2, 0x29, 0xC5, 0x8A,
        0xC6, 0x29, 0xC5, 0x39, 0xD5, 0x74, 0x0C, 0xBA,
        0x91, 0x01, 0xB4, 0x09, 0xCD, 0x21, 0xB8, 0xFF,
        0x4C, 0xCD, 0x21, 0x53, 0xB8, 0x53, 0x00, 0x50,
        0xCB, 0x2E, 0x8B, 0x2E, 0x08, 0x00, 0x8C, 0xDA,
        0x89, 0xE8, 0x3D, 0x00, 0x10, 0x76, 0x03, 0xB8,
        0x00, 0x10, 0x29, 0xC5, 0x29, 0xC2, 0x29, 0xC3,
        0x8E, 0xDA, 0x8E, 0xC3, 0xB1, 0x03, 0xD3, 0xE0,
        0x89, 0xC1, 0xD1, 0xE0, 0x48, 0x48, 0x8B, 0xF0,
        0x8B, 0xF8, 0xF3, 0xA5, 0x09, 0xED, 0x75, 0xD8,
        0xFC, 0x8E, 0xC2, 0x8E, 0xDB, 0x31, 0xF6, 0x31,
        0xFF, 0xBA, 0x10, 0x00, 0xAD, 0x89, 0xC5, 0xD1,
        0xED, 0x4A, 0x75, 0x05, 0xAD, 0x89, 0xC5, 0xB2,
        0x10, 0x73, 0x03, 0xA4, 0xEB, 0xF1, 0x31, 0xC9,
        0xD1, 0xED, 0x4A, 0x75, 0x05, 0xAD, 0x89, 0xC5,
        0xB2, 0x10, 0x72, 0x22, 0xD1, 0xED, 0x4A, 0x75,
        0x05, 0xAD, 0x89, 0xC5, 0xB2, 0x10, 0xD1, 0xD1,
        0xD1, 0xED, 0x4A, 0x75, 0x05, 0xAD, 0x89, 0xC5,
        0xB2, 0x10, 0xD1, 0xD1, 0x41, 0x41, 0xAC, 0xB7,
        0xFF, 0x8A, 0xD8, 0xE9, 0x13, 0x00, 0xAD, 0x8B,
        0xD8, 0xB1, 0x03, 0xD2, 0xEF, 0x80, 0xCF, 0xE0,
        0x80, 0xE4, 0x07, 0x74, 0x0C, 0x88, 0xE1, 0x41,
        0x41, 0x26, 0x8A, 0x01, 0xAA, 0xE2, 0xFA, 0xEB,
        0xA6, 0xAC, 0x08, 0xC0, 0x74, 0x40, 0x3C, 0x01,
        0x74, 0x05, 0x88, 0xC1, 0x41, 0xEB, 0xEA, 0x89
    }, sig91 = {
        0x06, 0x0E, 0x1F, 0x8B, 0x0E, 0x0C, 0x00, 0x8B,
        0xF1, 0x4E, 0x89, 0xF7, 0x8C, 0xDB, 0x03, 0x1E,
        0x0A, 0x00, 0x8E, 0xC3, 0xFD, 0xF3, 0xA4, 0x53,
        0xB8, 0x2B, 0x00, 0x50, 0xCB, 0x2E, 0x8B, 0x2E,
        0x08, 0x00, 0x8C, 0xDA, 0x89, 0xE8, 0x3D, 0x00,
        0x10, 0x76, 0x03, 0xB8, 0x00, 0x10, 0x29, 0xC5,
        0x29, 0xC2, 0x29, 0xC3, 0x8E, 0xDA, 0x8E, 0xC3,
        0xB1, 0x03, 0xD3, 0xE0, 0x89, 0xC1, 0xD1, 0xE0,
        0x48, 0x48, 0x8B, 0xF0, 0x8B, 0xF8, 0xF3, 0xA5,
        0x09, 0xED, 0x75, 0xD8, 0xFC, 0x8E, 0xC2, 0x8E,
        0xDB, 0x31, 0xF6, 0x31, 0xFF, 0xBA, 0x10, 0x00,
        0xAD, 0x89, 0xC5, 0xD1, 0xED, 0x4A, 0x75, 0x05,
        0xAD, 0x89, 0xC5, 0xB2, 0x10, 0x73, 0x03, 0xA4,
        0xEB, 0xF1, 0x31, 0xC9, 0xD1, 0xED, 0x4A, 0x75,
        0x05, 0xAD, 0x89, 0xC5, 0xB2, 0x10, 0x72, 0x22,
        0xD1, 0xED, 0x4A, 0x75, 0x05, 0xAD, 0x89, 0xC5,
        0xB2, 0x10, 0xD1, 0xD1, 0xD1, 0xED, 0x4A, 0x75,
        0x05, 0xAD, 0x89, 0xC5, 0xB2, 0x10, 0xD1, 0xD1,
        0x41, 0x41, 0xAC, 0xB7, 0xFF, 0x8A, 0xD8, 0xE9,
        0x13, 0x00, 0xAD, 0x8B, 0xD8, 0xB1, 0x03, 0xD2,
        0xEF, 0x80, 0xCF, 0xE0, 0x80, 0xE4, 0x07, 0x74,
        0x0C, 0x88, 0xE1, 0x41, 0x41, 0x26, 0x8A, 0x01,
        0xAA, 0xE2, 0xFA, 0xEB, 0xA6, 0xAC, 0x08, 0xC0,
        0x74, 0x34, 0x3C, 0x01, 0x74, 0x05, 0x88, 0xC1,
        0x41, 0xEB, 0xEA, 0x89, 0xFB, 0x83, 0xE7, 0x0F,
        0x81, 0xC7, 0x00, 0x20, 0xB1, 0x04, 0xD3, 0xEB,
        0x8C, 0xC0, 0x01, 0xD8, 0x2D, 0x00, 0x02, 0x8E,
        0xC0, 0x89, 0xF3, 0x83, 0xE6, 0x0F, 0xD3, 0xEB,
        0x8C, 0xD8, 0x01, 0xD8, 0x8E, 0xD8, 0xE9, 0x72
    }, sigbuf = new BYTE[sig90.Length];

    /* EXE header test (is it LZEXE file?) */
    static int rdhead(Stream ifile ,out int ver){
        long entry;
        /* v0.8 */
        /* v0.7 old code */
        /*  if(fread(ihead,sizeof ihead[0],0x10,ifile)!=0x10)
         *      return FAILURE;
         *  memcpy(ohead,ihead,sizeof ihead[0] * 0x10);
         *  if(ihead[0]!=0x5a4d || ihead[4]!=2 || ihead[0x0d]!=0)
         *      return FAILURE;
         *  if(ihead[0x0c]==0x1c && memcmp(&ihead[0x0e],"LZ09",4)==0){
         *      *ver=90; return SUCCESS ;
         *  }
         *  if(ihead[0x0c]==0x1c && memcmp(&ihead[0x0e],"LZ91",4)==0){
         *      *ver=91; return SUCCESS ;
         *  }
         */
        ver = 0;
        if (ifile.Read(ihead_buffer, 0, ihead_buffer.Length) != ihead_buffer.Length)	     /* v0.8 */
	    return FAILURE; 					     /* v0.8 */
        Array.Copy(ihead_buffer, ohead_buffer, ohead_buffer.Length);			     /* v0.8 */
        if((ihead [0] != 0x5a4d && ihead [0] != 0x4d5a) ||		     /* v0.8 */
           ihead [0x0d] != 0 || ihead [0x0c] != 0x1c)		     /* v0.8 */
	    return FAILURE; 					     /* v0.8 */
        entry = ((long) (ihead [4] + ihead[0x0b]) << 4) + ihead[0x0a];   /* v0.8 */
        ifile.Position = entry;
        if (ifile.Read(sigbuf, 0, sigbuf.Length) != sigbuf.Length)    /* v0.8 */
	    return FAILURE; 					     /* v0.8 */
        if (Enumerable.SequenceEqual(sigbuf, sig90)) {		     /* v0.8 */
	    ver = 90;						     /* v0.8 */
	    return SUCCESS; 					     /* v0.8 */
        }								     /* v0.8 */
        if (Enumerable.SequenceEqual(sigbuf, sig91)) {		     /* v0.8 */
	    ver = 91;						     /* v0.8 */
	    return SUCCESS; 					     /* v0.8 */
        }								     /* v0.8 */
        return FAILURE;
    }

    /* make relocation table */
    static int mkreltbl(BinaryReader ifile,BinaryWriter ofile,int ver) {
        long fpos;
        int i;

    /* v0.7 old code
     *  allocsize=((ihead[1]+16-1)>>4) + ((ihead[2]-1)<<5) - ihead[4] + ihead[5];
     */
        fpos=(long)(ihead[0x0b]+ihead[4])<<4;		/* goto CS:0000 */
        ifile.BaseStream.Position = fpos;
        ifile.Read(inf_buffer, 0, inf_buffer.Length);
        ohead[0x0a]=inf[0]; 	/* IP */
        ohead[0x0b]=inf[1]; 	/* CS */
        ohead[0x08]=inf[2]; 	/* SP */
        ohead[0x07]=inf[3]; 	/* SS */
        /* inf[4]:size of compressed load module (PARAGRAPH)*/
        /* inf[5]:increase of load module size (PARAGRAPH)*/
        /* inf[6]:size of decompressor with  compressed relocation table (BYTE) */
        /* inf[7]:check sum of decompresser with compressd relocation table(Ver.0.90) */
        ohead[0x0c]=0x1c;		/* start position of relocation table */
        ofile.BaseStream.Position = 0x1cL;
        switch(ver){
        case 90: i=reloc90(ifile,ofile,fpos);
                 break;
        case 91: i=reloc91(ifile,ofile,fpos);
                 break;
        default: i=FAILURE; break;
        }
        if(i!=SUCCESS){
            Console.WriteLine("error at relocation table.");
            return (FAILURE);
        }
        fpos = ofile.BaseStream.Position;
    /* v0.7 old code
     *  i= (int) fpos & 0x1ff;
     *  if(i) i=0x200-i;
     *  ohead[4]= (int) (fpos+i)>>4;
     */
        i= (0x200 - (int) fpos) & 0x1ff;	/* v0.7 */
        ohead[4]= unchecked((WORD) (int)((fpos+i)>>4));	/* v0.7 */

        for( ; i>0; i--)
            ofile.Write((byte)0);
        return SUCCESS;
    }
    /* for LZEXE ver 0.90 */
    static int reloc90(BinaryReader ifile,BinaryWriter ofile,long fpos) {
        uint c;
        WORD rel_count=0;
        WORD rel_seg,rel_off;

        ifile.BaseStream.Position = fpos + 0x19d;
    				    /* 0x19d=compressed relocation table address */
        rel_seg=0;
        do{
            if(ifile.BaseStream.Position >= ifile.BaseStream.Length) return FAILURE;
            c = ifile.ReadUInt16();            /* v0.9 */
            for(;c>0;c--) {
                rel_off = ifile.ReadUInt16();  /* v0.9 */
                ofile.Write(rel_off); /* v0.9 */
                ofile.Write(rel_seg); /* v0.9 */
                rel_count++;
            }
            rel_seg += 0x1000;
        } while(rel_seg!=0);
        ohead[3]=rel_count;
        return(SUCCESS);
    }
    /* for LZEXE ver 0.91*/
    static int reloc91(BinaryReader ifile,BinaryWriter ofile,long fpos) {
        WORD span;
        WORD rel_count=0;
        WORD rel_seg,rel_off;

        ifile.BaseStream.Position = fpos+0x158;
                                    /* 0x158=compressed relocation table address */
        rel_off=0; rel_seg=0;
        for(;;) {
            if (ifile.BaseStream.Position >= ifile.BaseStream.Length) return(FAILURE);
            if((span=(BYTE)ifile.ReadByte())==0) { /* v0.9 */
                span = ifile.ReadUInt16();    /* v0.9 */
                if(span==0){
                    rel_seg += 0x0fff;
                    continue;
                } else if(span==1){
                    break;
                }
            }
            rel_off += span;
            rel_seg += unchecked((WORD)((rel_off & ~0x0f)>>4));
            rel_off &= 0x0f;
            ofile.Write(rel_off);   /* v0.9 */
            ofile.Write(rel_seg);   /* v0.9 */
            rel_count++;
        }
        ohead[3]=rel_count;
        return(SUCCESS);
    }

    /*---------------------*/
    struct bitstream
    {
        public BinaryReader fp;
        public WORD buf;
        public BYTE count;
    }

    static BYTE[] data = new BYTE[0x4500];

    /*---------------------*/
    /* decompressor routine */
    static int unpack(BinaryReader ifile,BinaryWriter ofile){
        int len;
        WORD span;
        long fpos;
        var bits = default(bitstream);
        int p = 0;

        fpos=((long)ihead[0x0b]-(long)inf[4]+(long)ihead[4])<<4;
        ifile.BaseStream.Position = fpos;
        fpos=(long)ohead[4]<<4;
        ofile.BaseStream.Position = fpos;
        initbits(ref bits,ifile);
        Console.WriteLine(" unpacking. ");
        for(;;){
            if(p>0x4000){
                ofile.Write(data, 0, 0x2000);
                p-=0x2000;
                Array.Copy(data, 0x2000, data, 0, p);  /* v0.9 */
                Console.Write('.');
            }
            if(getbit(ref bits) != 0) {
                data[p++]=(BYTE)ifile.ReadByte();            /* v0.9 */
                continue;
            }
            if(getbit(ref bits) == 0) {
                len=getbit(ref bits) <<1;
                len |= getbit(ref bits);
                len += 2;
                span=unchecked((ushort)((BYTE)ifile.ReadByte() | 0xff00));   /* v0.9 */
            } else {
                span=(BYTE)ifile.ReadByte();
                len=(BYTE)ifile.ReadByte();             /* v0.9 */
                span = unchecked((ushort)(span | ((len & ~0x07)<<5) | 0xe000));
                len = (len & 0x07)+2;
                if (len==2) {
                    len=(BYTE)ifile.ReadByte();         /* v0.9 */

                    if(len==0)
                        break;    /* end mark of compreesed load module */

                    if(len==1)
                        continue; /* segment change */
                    else
                        len++;
                }
            }
            for( ;len>0;len--,p++){
                data[p]=data[p+unchecked((short)span)];             /* v0.9 */
            }
        }
        if(p!=0)
            ofile.Write(data,0,p);
        loadsize=ofile.BaseStream.Position-fpos;
        Console.WriteLine("end");
        return(SUCCESS);
    }

    /* write EXE header*/
    static void wrhead(BinaryWriter ofile) {
        if(ihead[6]!=0) {
            ohead[5]-= unchecked((ushort)(inf[5] + ((inf[6]+16-1)>>4) + 9));     /* v0.7 */
            if(ihead[6]!=0xffff)
                ohead[6]-=unchecked((ushort)(ihead[5]-ohead[5]));
        }
        ohead[1]=unchecked((ushort)(((WORD)loadsize+(ohead[4]<<4)) & 0x1ff));    /* v0.7 */
        ohead[2]=(WORD)((loadsize+((long)ohead[4]<<4)+0x1ff) >> 9); /* v0.7 */
        ofile.BaseStream.Position = 0;
        ofile.Write(ohead_buffer, 0, 0x0e * sizeof(WORD));
    }


    /*-------------------------------------------*/

    /* get compress information bit by bit */
    static void initbits(ref bitstream p,BinaryReader filep){
        p.fp=filep;
        p.count=0x10;
        p.buf = p.fp.ReadUInt16();     /* v0.9 */
        /* Console.WriteLine($"%04x ",p.buf); */
    }

    static int getbit(ref bitstream p) {
        int b;
        b = p.buf & 1;
        if(--p.count == 0){
            p.buf = p.fp.ReadUInt16(); /* v0.9 */
            /* Console.WriteLine($"%04x ",p.buf); */
            p.count= 0x10;
        }else
            p.buf >>= 1;

        return b;
    }
}