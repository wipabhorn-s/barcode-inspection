Option Strict On

Imports Barcode_Inspection.Domain
Imports Npgsql

Namespace Data

    Public Class ProductSpecRepository

        Private Const Columns As String =
            "product_name, item_code, barcode_spec, hinge_spec, quantity, modulus_34, modulus_36, dimension_check, electrical_check,
             appearance_check, hinge_check, cmos_check, compare_check, machine_check, running_check"

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        Public Async Function GetAllAsync() As Task(Of List(Of ProductSpec))
            Dim products As New List(Of ProductSpec)

            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"SELECT {Columns} FROM barcode_inspection.product_spec ORDER BY product_name ASC", conn)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        While Await reader.ReadAsync().ConfigureAwait(False)
                            products.Add(Read(reader))
                        End While
                    End Using
                End Using
            End Using

            Return products
        End Function

        ''' <summary>Returns the product's spec, or Nothing when the product does not exist.</summary>
        Public Async Function GetAsync(productName As String) As Task(Of ProductSpec)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"SELECT {Columns} FROM barcode_inspection.product_spec WHERE product_name = @product_name", conn)
                    cmd.Parameters.AddWithValue("@product_name", productName)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        If Not Await reader.ReadAsync().ConfigureAwait(False) Then Return Nothing
                        Return Read(reader)
                    End Using
                End Using
            End Using
        End Function

        ''' <exception cref="PostgresException">SqlState 23505 when the product name already exists.</exception>
        Public Function InsertAsync(spec As ProductSpec) As Task
            Return ExecuteAsync(
                $"INSERT INTO barcode_inspection.product_spec ({Columns})
                  VALUES (@product_name, @item_code, @barcode_spec, @hinge_spec, @quantity, @modulus_34, @modulus_36, @dimension_check,
                          @electrical_check, @appearance_check, @hinge_check, @cmos_check, @compare_check, @machine_check, @running_check)",
                spec, Nothing)
        End Function

        ''' <summary>Saves <paramref name="spec"/> over the product currently called <paramref name="currentName"/> (it may be renamed).</summary>
        ''' <exception cref="PostgresException">SqlState 23505 when the new name belongs to another product.</exception>
        Public Function UpdateAsync(currentName As String, spec As ProductSpec) As Task(Of Integer)
            Return ExecuteAsync(
                "UPDATE barcode_inspection.product_spec
                 SET product_name = @product_name, item_code = @item_code, barcode_spec = @barcode_spec, hinge_spec = @hinge_spec,
                     quantity = @quantity, modulus_34 = @modulus_34, modulus_36 = @modulus_36, dimension_check = @dimension_check,
                     electrical_check = @electrical_check, appearance_check = @appearance_check, hinge_check = @hinge_check,
                     cmos_check = @cmos_check, compare_check = @compare_check, machine_check = @machine_check, running_check = @running_check
                 WHERE product_name = @current_name",
                spec, currentName)
        End Function

        ''' <summary>Returns False when no such product exists.</summary>
        ''' <exception cref="PostgresException">SqlState 23503 when inspection records still use the product.</exception>
        Public Async Function DeleteAsync(productName As String) As Task(Of Boolean)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("DELETE FROM barcode_inspection.product_spec WHERE product_name = @product_name", conn)
                    cmd.Parameters.AddWithValue("@product_name", productName)
                    Return Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False) > 0
                End Using
            End Using
        End Function

        Private Async Function ExecuteAsync(sql As String, spec As ProductSpec, currentName As String) As Task(Of Integer)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    With cmd.Parameters
                        .AddWithValue("@product_name", spec.ProductName)
                        .AddWithValue("@item_code", spec.ItemCode)
                        .AddWithValue("@barcode_spec", spec.BarcodeSpec)
                        .AddWithValue("@hinge_spec", If(String.IsNullOrWhiteSpace(spec.HingeSpec), DBNull.Value, CObj(spec.HingeSpec)))
                        .AddWithValue("@quantity", spec.Quantity)
                        .AddWithValue("@modulus_34", spec.Modulus34)
                        .AddWithValue("@modulus_36", spec.Modulus36)
                        .AddWithValue("@dimension_check", spec.DimensionCheck)
                        .AddWithValue("@electrical_check", spec.ElectricalCheck)
                        .AddWithValue("@appearance_check", spec.AppearanceCheck)
                        .AddWithValue("@hinge_check", spec.HingeCheck)
                        .AddWithValue("@cmos_check", spec.CmosCheck)
                        .AddWithValue("@compare_check", spec.CompareCheck)
                        .AddWithValue("@machine_check", spec.MachineCheck)
                        .AddWithValue("@running_check", spec.RunningCheck)
                        If currentName IsNot Nothing Then .AddWithValue("@current_name", currentName)
                    End With
                    Return Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

        Private Shared Function Read(reader As NpgsqlDataReader) As ProductSpec
            Return New ProductSpec With {
                .ProductName = TextOrEmpty(reader, "product_name"),
                .ItemCode = TextOrEmpty(reader, "item_code"),
                .BarcodeSpec = TextOrEmpty(reader, "barcode_spec"),
                .HingeSpec = TextOrEmpty(reader, "hinge_spec"),
                .Quantity = If(IsDBNull(reader("quantity")), 0, Convert.ToInt32(reader("quantity"))),
                .Modulus34 = Flag(reader, "modulus_34"),
                .Modulus36 = Flag(reader, "modulus_36"),
                .DimensionCheck = Flag(reader, "dimension_check"),
                .ElectricalCheck = Flag(reader, "electrical_check"),
                .AppearanceCheck = Flag(reader, "appearance_check"),
                .HingeCheck = Flag(reader, "hinge_check"),
                .CmosCheck = Flag(reader, "cmos_check"),
                .CompareCheck = Flag(reader, "compare_check"),
                .MachineCheck = Flag(reader, "machine_check"),
                .RunningCheck = Flag(reader, "running_check")
            }
        End Function

        Private Shared Function TextOrEmpty(reader As NpgsqlDataReader, column As String) As String
            Return If(IsDBNull(reader(column)), "", reader(column).ToString())
        End Function

        Private Shared Function Flag(reader As NpgsqlDataReader, column As String) As Boolean
            Return Not IsDBNull(reader(column)) AndAlso Convert.ToBoolean(reader(column))
        End Function

    End Class

End Namespace
