﻿Imports System.IO
Imports System.Threading
Imports Algo2TradeBLL

Public Class HighATRStocksWithMultiplier
    Inherits StockSelection

    Public Sub New(ByVal canceller As CancellationTokenSource,
                   ByVal cmn As Common,
                   ByVal stockType As Integer)
        MyBase.New(canceller, cmn, stockType)
    End Sub

    Public Overrides Async Function GetStockDataAsync(ByVal startDate As Date, ByVal endDate As Date) As Task(Of DataTable)
        Await Task.Delay(0).ConfigureAwait(False)
        Dim ret As New DataTable
        ret.Columns.Add("Date")
        ret.Columns.Add("Trading Symbol")
        ret.Columns.Add("Lot Size")
        ret.Columns.Add("ATR %")
        ret.Columns.Add("Blank Candle %")
        ret.Columns.Add("Day ATR")
        ret.Columns.Add("Previous Day Open")
        ret.Columns.Add("Previous Day Low")
        ret.Columns.Add("Previous Day High")
        ret.Columns.Add("Previous Day Close")
        ret.Columns.Add("Current Day Close")
        ret.Columns.Add("Slab")
        ret.Columns.Add("Multiplier")

        Using atrStock As New ATRStockSelection(_canceller)
            AddHandler atrStock.Heartbeat, AddressOf OnHeartbeat

            Dim tradingDate As Date = startDate
            While tradingDate <= endDate
                _bannedStockFileName = Path.Combine(My.Application.Info.DirectoryPath, String.Format("Bannned Stocks {0}.csv", tradingDate.ToString("ddMMyyyy")))
                For Each runningFile In Directory.GetFiles(My.Application.Info.DirectoryPath, "Bannned Stocks *.csv")
                    If Not runningFile.Contains(tradingDate.ToString("ddMMyyyy")) Then File.Delete(runningFile)
                Next
                Dim bannedStockList As List(Of String) = Nothing
                Using bannedStock As New BannedStockDataFetcher(_bannedStockFileName, _canceller)
                    AddHandler bannedStock.Heartbeat, AddressOf OnHeartbeat
                    bannedStockList = Await bannedStock.GetBannedStocksData(tradingDate).ConfigureAwait(False)
                End Using

                Dim atrStockList As Dictionary(Of String, InstrumentDetails) = Await atrStock.GetATRStockData(_eodTable, tradingDate, bannedStockList, False).ConfigureAwait(False)
                If atrStockList IsNot Nothing AndAlso atrStockList.Count > 0 Then
                    Dim stockCounter As Integer = 0
                    For Each runningStock In atrStockList
                        _canceller.Token.ThrowIfCancellationRequested()
                        Dim intradayPayload As Dictionary(Of Date, Payload) = _cmn.GetRawPayloadForSpecificTradingSymbol(_intradayTable, runningStock.Value.TradingSymbol, tradingDate.AddDays(-8), tradingDate.AddDays(-1))
                        If intradayPayload IsNot Nothing AndAlso intradayPayload.Count > 100 Then
                            Dim lastTradingDay As Date = intradayPayload.LastOrDefault.Key.Date
                            Dim hkPayload As Dictionary(Of Date, Payload) = Nothing
                            Indicator.HeikenAshi.ConvertToHeikenAshi(intradayPayload, hkPayload)
                            Dim atrPayload As Dictionary(Of Date, Decimal) = Nothing
                            Indicator.ATR.CalculateATR(14, hkPayload, atrPayload)
                            Dim highestATR As Decimal = atrPayload.Max(Function(x)
                                                                           If x.Key.Date = lastTradingDay.Date Then
                                                                               Return x.Value
                                                                           Else
                                                                               Return Decimal.MinValue
                                                                           End If
                                                                       End Function)
                            Dim slPoint As Decimal = Utilities.Numbers.ConvertFloorCeling(highestATR, 0.05, Utilities.Numbers.NumberManipulation.RoundOfType.Celing)
                            Dim price As Decimal = Utilities.Numbers.ConvertFloorCeling(runningStock.Value.PreviousDayClose, 0.05, Utilities.Numbers.NumberManipulation.RoundOfType.Celing)
                            Dim quantity As Integer = CalculateQuantityFromStoploss(price, price - slPoint, -500)
                            Dim target As Decimal = CalculateTarget(price, quantity, 500) - price

                            Dim row As DataRow = ret.NewRow
                            row("Date") = tradingDate.ToString("dd-MM-yyyy")
                            row("Trading Symbol") = runningStock.Value.TradingSymbol
                            row("Lot Size") = runningStock.Value.LotSize
                            row("ATR %") = Math.Round(runningStock.Value.ATRPercentage, 4)
                            row("Blank Candle %") = runningStock.Value.BlankCandlePercentage
                            row("Day ATR") = Math.Round(runningStock.Value.DayATR, 4)
                            row("Previous Day Open") = runningStock.Value.PreviousDayOpen
                            row("Previous Day Low") = runningStock.Value.PreviousDayLow
                            row("Previous Day High") = runningStock.Value.PreviousDayHigh
                            row("Previous Day Close") = runningStock.Value.PreviousDayClose
                            row("Current Day Close") = runningStock.Value.CurrentDayClose
                            row("Slab") = runningStock.Value.Slab
                            row("Multiplier") = Math.Round(target / slPoint, 3)

                            ret.Rows.Add(row)
                            stockCounter += 1
                            If stockCounter = My.Settings.NumberOfStockPerDay Then Exit For
                        End If
                    Next
                End If

                tradingDate = tradingDate.AddDays(1)
            End While
        End Using
        Return ret
    End Function

    Public Function CalculateQuantityFromStoploss(ByVal buyPrice As Decimal, ByVal sellPrice As Decimal, ByVal netLossPerTrade As Decimal) As Integer
        Dim ret As Integer = 1

        Dim calculator As New Calculator.BrokerageCalculator(_canceller)
        For quantity = 1 To Integer.MaxValue
            Dim potentialBrokerage As Calculator.BrokerageAttributes = New Calculator.BrokerageAttributes
            calculator.Intraday_Equity(buyPrice, sellPrice, quantity, potentialBrokerage)

            If potentialBrokerage.NetProfitLoss < netLossPerTrade Then
                Exit For
            Else
                ret = quantity
            End If
        Next
        Return ret
    End Function

    Public Function CalculateTarget(ByVal entryPrice As Decimal, ByVal quantity As Integer, ByVal desiredProfitOfTrade As Decimal) As Decimal
        Dim potentialBrokerage As Calculator.BrokerageAttributes = Nothing
        Dim calculator As New Calculator.BrokerageCalculator(_canceller)

        Dim ret As Decimal = entryPrice
        potentialBrokerage = New Calculator.BrokerageAttributes
        While Not potentialBrokerage.NetProfitLoss > desiredProfitOfTrade
            calculator.Intraday_Equity(entryPrice, ret, quantity, potentialBrokerage)
            If potentialBrokerage.NetProfitLoss > desiredProfitOfTrade Then Exit While
            ret += 0.05
        End While

        Return ret
    End Function
End Class