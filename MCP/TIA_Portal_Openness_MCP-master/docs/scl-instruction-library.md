# SCL 指令库（中性参考）

本文件汇总在 S7-1200 / S7-1500 SCL 中常用的语法和指令模板，便于 `PlcBuildAndImport(kind=fc|fb)` 的 DSL 转换，或直接写入外部源 `.scl` 文件后通过 `ImportPlcExternalSource` + `GenerateBlocksFromExternalSource` 导入。

所有示例均为通用语法，**不绑定任何特定工艺或设备名**。复制到实际工程时按需修改变量名与数据范围。

## 1. 基本表达与控制流

```scl
// 赋值与运算
#Out := #A + #B - #C;
#Pct := 100.0 * #Value / #Range;

// 条件
IF #Enable AND NOT #Fault THEN
    #Run := TRUE;
ELSIF #Pause THEN
    #Run := FALSE;
ELSE
    #Run := FALSE;
END_IF;

// 多分支
CASE #Mode OF
    0:  #SP := 0.0;
    1:  #SP := #SP_Manual;
    2:  #SP := #SP_Auto;
ELSE
    #SP := 0.0;
END_CASE;

// 循环
FOR #i := 0 TO 9 DO
    #Sum := #Sum + #Array[#i];
END_FOR;

// 当条件成立
WHILE #Counter < #Preset DO
    #Counter := #Counter + 1;
END_WHILE;
```

## 2. 类型转换与缩放

| 用途 | 指令 |
|------|------|
| Int ↔ Real | `INT_TO_REAL`、`REAL_TO_INT` |
| DInt ↔ Real | `DINT_TO_REAL`、`REAL_TO_DINT` |
| 标准化到 0~1 | `NORM_X(MIN, VALUE, MAX)` |
| 反标准化到工程量 | `SCALE_X(MIN, NORM, MAX)` |
| 限幅 | `LIMIT(MN, IN, MX)` |
| 绝对值 | `ABS(...)` |

```scl
// 模拟量缩放（0~27648 → 0~100.0）
#Norm   := NORM_X(MIN := 0,    VALUE := #RawAI, MAX := 27648);
#Engineering := SCALE_X(MIN := 0.0, VALUE := #Norm,  MAX := 100.0);
#Limited := LIMIT(MN := 0.0, IN := #Engineering, MX := 100.0);
```

## 3. 沿检测

```scl
// 实例化 R_TRIG / F_TRIG（声明在 Static 区，便于实例保留）
#RisingStart(CLK := #Cmd_Start);
IF #RisingStart.Q THEN
    #PulseCount := #PulseCount + 1;
END_IF;
```

## 4. 定时器（IEC）

```scl
// TON 通电延时（实例在 FB 的 Static 或独立 DB 中）
#Ton1(IN := #Cmd_Run, PT := T#3S);
#Delayed := #Ton1.Q;

// TOF 断电延时
#Tof1(IN := #Cmd_Run, PT := T#1S);

// TP 单脉冲
#Tp1(IN := #Trigger, PT := T#500MS);
```

> 在 FC 内不能创建 IEC 定时器实例（特别是 F-CPU），把实例放在 **FB Static 段** 或 **全局 DB** 中。

## 5. 计数器（IEC）

```scl
#Ctu1(CU := #Cmd_Inc, R := #Cmd_Clear, PV := #Preset);
#Value := #Ctu1.CV;
#Done  := #Ctu1.Q;
```

## 6. PID_Compact 调用模式（仅展示参数接口）

```scl
"PID_Compact_1"(
    Setpoint  := #SP,
    Input     := #PV,
    Output    => #OUT,
    ManualEnable := #Mode_Manual,
    ManualValue  := #ManOut,
    Reset     := #Cmd_Reset
);
```

参数说明（节选）：
- `Setpoint` / `Input` 必填，类型 `Real`；
- `Output` 为运算输出；
- `ManualEnable=TRUE` 时执行手动；
- `Reset=TRUE` 切到「未激活」状态；
- 其余参数（`Mode`、`PIDStatus`、`Error`）按需读取。

## 7. 安全比较与死区

```scl
// 死区比较
#Diff := ABS(#SP - #PV);
IF #Diff <= #Deadband THEN
    #Reached := TRUE;
ELSE
    #Reached := FALSE;
END_IF;

// 三态比较
IF #PV > #HighLimit THEN
    #Level := 2;
ELSIF #PV >= #LowLimit THEN
    #Level := 1;
ELSE
    #Level := 0;
END_IF;
```

## 8. 斜坡 / 速度限幅（通用模板）

```scl
// 每周期最大变化量（受扫描时间影响）
IF #Target > #Current + #RampUp THEN
    #Current := #Current + #RampUp;
ELSIF #Target < #Current - #RampDown THEN
    #Current := #Current - #RampDown;
ELSE
    #Current := #Target;
END_IF;
```

## 9. 数组与 FOR-EACH 风格

```scl
// 求和
#Sum := 0.0;
FOR #i := 0 TO 9 DO
    #Sum := #Sum + #Buffer[#i];
END_FOR;
#Avg := #Sum / 10.0;

// 最大值
#Max := #Buffer[0];
FOR #i := 1 TO 9 DO
    IF #Buffer[#i] > #Max THEN
        #Max := #Buffer[#i];
    END_IF;
END_FOR;
```

## 10. UDT 引用

```scl
// 假设 UDT_BasicStatus 中包含 Active/Error/Setpoint/Actual
#Item.Active   := #Run;
#Item.Error    := #Fault;
#Item.Setpoint := #SP;
#Item.Actual   := #PV;
```

## 11. 字符串拼接（仅 1500/部分 1200 支持）

```scl
#Msg := CONCAT(IN1 := 'STEP=', IN2 := DINT_TO_STRING(#Step));
```

## 12. 错误码与日志（建议模式）

```scl
IF #SensorErr THEN
    #ErrorCode := 1001;
    #Status    := 'Sensor lost';
ELSIF #DriveErr THEN
    #ErrorCode := 1002;
    #Status    := 'Drive fault';
ELSE
    #ErrorCode := 0;
    #Status    := 'OK';
END_IF;
```

## 13. DSL 适配性（`PlcBuildAndImport(kind=fc|fb)`）

DSL 直接支持 `assignment`、`if/elsif/else/endif`、`line`、`token`、`literal`。
**不支持** 的语法（`FOR`/`WHILE`/`CASE`/`REPEAT`/`EXIT`/`CONTINUE`/`RETURN`）请用：

- **外部 SCL 源**：将完整 `.scl` 写到磁盘（UTF-8 + BOM），通过 `ImportPlcExternalSource` 然后 `GenerateBlocksFromExternalSource`；
- 或在 TIA 中编辑后 `ExportBlock`，再用 `ImportBlock` 入仓。
