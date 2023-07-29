using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Meta.Numerics.Matrices;
using Meta.Numerics;

namespace FitEllipse
{
	
    public class Point
    {
        public double X { get; set; }
        public double Y {get; set;  }
    }

    public class PointCollection : List<Point>
    {
    }	
	
    public class Matrix
    {
        public double[,] data;
        int rows;
        int columns;
        public Matrix()
            : this(3, 3)
        {
        }

        public Matrix(int rows, int columns)
        {
            data = new double[rows, columns];
            this.rows = rows;
            this.columns = columns;
        }

        public Matrix(int rowCol)
            : this(rowCol, rowCol)
        {
        }

        public double this[int x, int y]
        {
            get
            {
                return data[x, y];
            }
            set
            {
                data[x, y] = value;
            }
        }

        public int Rows
        {

            get
            {
                return rows;
            }
        }

        public int Columns
        {

            get
            {
                return columns;
            }
        }

        public static Matrix operator +(Matrix a, Matrix b)
        {
            if (a.rows != b.rows || a.columns != b.columns)
                throw new ArgumentException("The rows and columns do not match for the matrices");
            Matrix c = new Matrix(a.rows, a.columns);
            for (int i = 0; i < a.rows; i++)
                for (int j = 0; j < a.columns; j++)
                    c[i, j] = a[i, j] + b[i, j];

            return c;
        }

        public static Matrix operator -(Matrix a, Matrix b)
        {
            if (a.rows != b.rows || a.columns != b.columns)
                throw new ArgumentException("The rows and columns do not match for the matrices");
            Matrix c = new Matrix(a.rows, a.columns);
            for (int i = 0; i < a.rows; i++)
                for (int j = 0; j < a.columns; j++)
                    c[i, j] = a[i, j] - b[i, j];

            return c;
        }

        public static Matrix operator *(Matrix a, Matrix b)
        {
            if (a.columns != b.rows)
                throw new ArgumentException("The rows and columns do not match for the matrices");
            double sum;
            Matrix c = new Matrix(a.rows, b.columns);
            for (int i = 0; i < a.rows; i++)
                for (int j = 0; j < b.columns; j++)
                {
                    sum = 0;
                    for (int k = 0; k < a.columns; k++)
                    {
                        sum += a[i, k] * b[k, j];
                    }
                    c[i, j] = sum;
                }

            return c;
        }

        public Matrix GetTranspose()
        {
            Matrix transpose = new Matrix(columns, rows);

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < columns; j++)
                    transpose[j, i] = data[i, j];

            return transpose;
        }
    }	
	
    public class EllipseFit
    {
        public Matrix Fit(PointCollection points)
        {
            int numPoints = points.Count;

            Matrix D1 = new Matrix(numPoints, 3);
            Matrix D2 = new Matrix(numPoints, 3);
            SquareMatrix S1 = new SquareMatrix(3);
            SquareMatrix S2 = new SquareMatrix(3);
            SquareMatrix S3 = new SquareMatrix(3);
            SquareMatrix T = new SquareMatrix(3);
            SquareMatrix M = new SquareMatrix(3);
            SquareMatrix C1 = new SquareMatrix(3);
            Matrix a1 = new Matrix(3, 1);
            Matrix a2 = new Matrix(3, 1);
            Matrix result = new Matrix(8, 1);
            Matrix temp;

            C1[0, 0] = 0;
            C1[0, 1] = 0;
            C1[0, 2] = 0.5;
            C1[1, 0] = 0;
            C1[1, 1] = -1;
            C1[1, 2] = 0;
            C1[2, 0] = 0.5;
            C1[2, 1] = 0;
            C1[2, 2] = 0;

            //2 D1 = [x .ˆ 2, x .* y, y .ˆ 2]; % quadratic part of the design matrix
            //3 D2 = [x, y, ones(size(x))]; % linear part of the design matrix
            for (int xx = 0; xx < points.Count; xx++)
            {
                Point p = points[xx];
                D1[xx, 0] = p.X * p.X;
                D1[xx, 1] = p.X * p.Y;
                D1[xx, 2] = p.Y * p.Y;

                D2[xx, 0] = p.X;
                D2[xx, 1] = p.Y;
                D2[xx, 2] = 1;
            }

            //4 S1 = D1’ * D1; % quadratic part of the scatter matrix
            temp = D1.GetTranspose() * D1;
            for (int xx = 0; xx < 3; xx++)
                for (int yy = 0; yy < 3; yy++)
                    S1[xx, yy] = temp[xx, yy];

            //5 S2 = D1’ * D2; % combined part of the scatter matrix
            temp = D1.GetTranspose() * D2;
            for (int xx = 0; xx < 3; xx++)
                for (int yy = 0; yy < 3; yy++)
                    S2[xx, yy] = temp[xx, yy];

            //6 S3 = D2’ * D2; % linear part of the scatter matrix
            temp = D2.GetTranspose() * D2;
            for (int xx = 0; xx < 3; xx++)
                for (int yy = 0; yy < 3; yy++)
                    S3[xx, yy] = temp[xx, yy];

            //7 T = - inv(S3) * S2’; % for getting a2 from a1
            if ( S2[0, 0] == 0.0 ) {
                return result;
            }
            try {
                T = -1 * S3.Inverse() * S2.Transpose();
            } catch ( System.DivideByZeroException ) {
                return result;
            }
            
            //8 M = S1 + S2 * T; % reduced scatter matrix
            M = S1 + S2 * T;
            
            //9 M = [M(3, :) ./ 2; - M(2, :); M(1, :) ./ 2]; % premultiply by inv(C1)
            M = C1 * M;
            
            //10 [evec, eval] = eig(M); % solve eigensystem
            ComplexEigensystem eigenSystem = M.Eigensystem();

            //11 cond = 4 * evec(1, :) .* evec(3, :) - evec(2, :) .ˆ 2; % evaluate a’Ca
            //12 a1 = evec(:, find(cond > 0)); % eigenvector for min. pos. eigenvalue
            for ( int xx = 0; xx < eigenSystem.Dimension; xx++ ) {
               Vector<Complex> vector =  eigenSystem.Eigenvector(xx);
               Complex condition = 4 * vector[0] * vector[2] - vector[1] * vector[1];
               if (condition.Im == 0 && condition.Re > 0) {
                   // Solution is found
//                   Console.WriteLine("\nSolution Found!");
                   for (int yy = 0; yy < vector.Count(); yy++) { 
//                       Console.Write("{0}, ", vector[yy]);
                        a1[yy, 0] = vector[yy].Re;
                   }
               }
            }
            //13 a2 = T * a1; % ellipse coefficients
//            a2 = T * a1;

            //14 a = [a1; a2]; % ellipse coefficients
            result[0, 0] = a1[0, 0];
            result[1, 0] = a1[1, 0];
            result[2, 0] = a1[2, 0];
            // https://de.wikipedia.org/wiki/Matrix-Vektor-Produkt
            result[3, 0] = T[0, 0] * a1[0, 0] + T[0, 1] * a1[1, 0] + T[0, 2] * a1[2, 0];
            result[4, 0] = T[1, 0] * a1[0, 0] + T[1, 1] * a1[1, 0] + T[1, 2] * a1[2, 0];
            result[5, 0] = T[2, 0] * a1[0, 0] + T[2, 1] * a1[1, 0] + T[2, 2] * a1[2, 0];
            //result[3, 0] = a2[0, 0];
            //result[4, 0] = a2[1, 0];
            //result[5, 0] = a2[2, 0];

            double a = a1[0, 0];
            double b = a1[1, 0];
            double c = a1[2, 0];
            double d = T[0, 0] * a1[0, 0] + T[0, 1] * a1[1, 0] + T[0, 2] * a1[2, 0];
            double e = T[1, 0] * a1[0, 0] + T[1, 1] * a1[1, 0] + T[1, 2] * a1[2, 0];
            double f = T[2, 0] * a1[0, 0] + T[2, 1] * a1[1, 0] + T[2, 2] * a1[2, 0];
            double h = (2 * c * d - e * b) / (b * b - 4 * a * c);
            double k = (-2 * a * h - d) / (b);

            // aX2 + bXY + cY2 + dX + eY + f = 0
            result[0, 0] = a;
            result[1, 0] = b; 
            result[2, 0] = c;
            result[3, 0] = d; 
            result[4, 0] = e; 
            result[5, 0] = f; 
            result[6, 0] = h;    // center X
            result[7, 0] = k;    // center Y

            return result;
        }
    }
}
